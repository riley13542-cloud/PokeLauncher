using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PokeLauncher.Models;

namespace PokeLauncher.Services;

/// <summary>What a download is doing right now; drives the Settings progress UI.</summary>
public sealed class InstallProgress
{
    public const string StageDownloading = "Downloading";
    public const string StageVerifying = "Verifying";
    public const string StageExtracting = "Extracting";
    public const string StageConfiguring = "Configuring";
    public const string StageDone = "Done";

    public InstallProgress(string stage, double percent, string? detail = null)
    {
        Stage = stage;
        Percent = percent;
        Detail = detail;
    }

    /// <summary>One of the Stage* constants, plus any follow-up text appended by callers.</summary>
    public string Stage { get; }

    /// <summary>Overall install progress, 0–100.</summary>
    public double Percent { get; }

    /// <summary>Optional detail (e.g. "3.2 of 13.5 MB").</summary>
    public string? Detail { get; init; }
}

/// <summary>Outcome of one install attempt.</summary>
public sealed class InstallOutcome
{
    public static InstallOutcome Cancelled() =>
        new() { Success = false, Error = "Download canceled — nothing was installed.", IsCancellation = true };

    public static InstallOutcome Failure(string error) => new() { Success = false, Error = error };

    public string? ExecutablePath { get; init; }

    public string? Error { get; init; }

    public bool Success { get; init; }

    public bool IsCancellation { get; init; }
}

/// <summary>Why a cache/record entry is considered broken or in need of a refresh.</summary>
public enum EmulatorInstallState
{
    /// <summary>The manifest does not offer a download for this system.</summary>
    NotOffered,
    /// <summary>Available for download.</summary>
    NotInstalled,
    /// <summary>A recorded install matches the manifest version and its executable exists.</summary>
    UpToDate,
    /// <summary>A newer manifest version is available.</summary>
    UpdateAvailable,
    /// <summary>An install is recorded, but the executable vanished (manual deletion?).</summary>
    Broken,
    /// <summary>This emulator is currently mid-download/extract in the app session.</summary>
    InProgress,
}

/// <summary>
/// Consent-driven emulator downloader: takes a manifest entry the user explicitly
/// confirmed, streams the archive from its official URL, verifies SHA-256 before
/// touching the install area, extracts atomically (temp dir, then rename), points the
/// existing emulator_configs rows at the extracted executable, and records provenance in
/// the emulator_downloads table. Official-source downloads only; no mirrors, and nothing
/// runs until the launcher itself is used later.
/// </summary>
public sealed class EmulatorDownloadService
{
    private readonly AppDatabase _database;
    private readonly EmulatorManifestLoadResult _manifest;

    // Static so repeated installs re-use one connection pool. HttpClient has no global
    // timeout here on purpose: a slow official-host download must finish; the caller's
    // CancellationToken (from the UI's Cancel button) is the real brake.
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly string _rootDir;
    private readonly string _downloadsDir;

    public EmulatorDownloadService(
        AppDatabase database,
        EmulatorCatalog catalog,
        HttpClient? httpClient = null,
        string? emulatorsRootOverride = null)
    {
        _database = database;
        _manifest = catalog.Load();
        _httpClient = httpClient ?? SharedHttpClient;
        _rootDir = emulatorsRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeLauncher", "Emulators");
        _downloadsDir = Path.Combine(_rootDir, "downloads");
        try
        {
            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_downloadsDir);
            CleanupStaleTemp();
        }
        catch (Exception exception)
        {
            ManifestStartupError = $"PokeLauncher could not prepare its emulator folder ({_rootDir}): {exception.Message}";
        }
    }

    private readonly HttpClient _httpClient;

    /// <summary>Manifest problems (invalid entries, override fallback…), shown once in Settings.</summary>
    public IReadOnlyList<string> ManifestErrors => _manifest.Errors;

    public bool ManifestLoaded => _manifest.Manifest is not null;

    /// <summary>Unusual setup problem (e.g. emulator folder unusable); null when fine.</summary>
    public string? ManifestStartupError { get; }

    /// <summary>%LocalAppData%\PokeLauncher\Emulators — where installs live.</summary>
    public string RootDirectory => _rootDir;

    // ------------------------------------------------------------ lookups

    public EmulatorManifestEntry? GetEntryForSystem(string systemKey) =>
        EmulatorCatalog.GetEntryForSystem(_manifest.Manifest, systemKey);

    /// <summary>The recorded download for the emulator able to run this system key, or null.</summary>
    public EmulatorDownloadRecord? GetRecordForSystem(string systemKey) =>
        GetRecordForEmulatorId(GetEntryForSystem(systemKey)?.Id);

    public EmulatorDownloadRecord? GetRecordForEmulatorId(string? emulatorId)
    {
        if (string.IsNullOrEmpty(emulatorId))
        {
            return null;
        }
        return _database.LoadEmulatorDownloads().TryGetValue(emulatorId, out var record) ? record : null;
    }

    /// <summary>
    /// Fully resolved install state: compares the manifest offer, the recorded install,
    /// and what's actually on disk right now.
    /// </summary>
    public EmulatorInstallState GetInstallState(string systemKey, out EmulatorDownloadRecord? record, out EmulatorManifestEntry? entry)
    {
        record = null;
        entry = GetEntryForSystem(systemKey);
        if (entry is null)
        {
            return EmulatorInstallState.NotOffered;
        }
        if (_inProgressIds.Contains(entry.Id))
        {
            return EmulatorInstallState.InProgress;
        }
        record = GetRecordForEmulatorId(entry.Id);
        if (record is null)
        {
            return EmulatorInstallState.NotInstalled;
        }
        if (!File.Exists(record.ExecutablePath))
        {
            return EmulatorInstallState.Broken;
        }
        return record.Version == entry.Version
            ? EmulatorInstallState.UpToDate
            : EmulatorInstallState.UpdateAvailable;
    }

    // ------------------------------------------------------------ install

    /// <summary>Gets whether a specific manifest entry is mid-install right now.</summary>
    public bool IsInProgress(string emulatorId) => _inProgressIds.Contains(emulatorId);

    private readonly HashSet<string> _inProgressIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads + verifies + extracts + configures one manifest entry the user has
    /// already consented to. Progress is reported through <paramref name="progress"/>
    /// (posted to the caller's sync context; run the call on the UI thread of the caller
    /// and updates stay on the UI thread).
    /// </summary>
    public async Task<InstallOutcome> InstallAsync(
        EmulatorManifestEntry entry,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!_inProgressIds.Add(entry.Id))
        {
            return InstallOutcome.Failure($"{entry.Name} is already being downloaded.");
        }
        try
        {
            return await InstallCoreAsync(entry, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return InstallOutcome.Cancelled();
        }
        finally
        {
            _inProgressIds.Remove(entry.Id);
        }
    }

    private async Task<InstallOutcome> InstallCoreAsync(
        EmulatorManifestEntry entry,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!_database.IsPersistent)
        {
            // The database layer already surfaces persistence problems; installing could
            // scatter files we cannot then record or wire up. Refuse honestly instead.
            return InstallOutcome.Failure(
                "The local database is unavailable, so PokeLauncher cannot record an emulator install. Nothing was downloaded.");
        }

        var download = entry.Download;
        Directory.CreateDirectory(_rootDir);
        Directory.CreateDirectory(_downloadsDir);

        // Install layout: ...\Emulators\<primary system>\<name>-<version>\
        // <primary system> is the entry's first system key, so mGBA installs once,
        // under GB, and is wired into GB + GBC + GBA configs.
        var primarySystem = entry.Systems.Count > 0 ? entry.Systems[0] : entry.Id;
        var installDir = Path.Combine(_rootDir, SanitizeFolderName(primarySystem),
            SanitizeFolderName($"{entry.Name}-{entry.Version}"));

        // ---- 1. download + hash in one pass
        var archivePath = Path.Combine(_downloadsDir, $"{entry.Id}-{entry.Version}.part");
        long? totalBytes = download.SizeBytes > 0 ? download.SizeBytes : null;
        string checksum;
        try
        {
            progress?.Report(new InstallProgress(InstallProgress.StageDownloading, 0));
            checksum = await DownloadAndHashAsync(download.Url, archivePath, totalBytes, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(archivePath);
            throw;
        }
        catch (Exception exception)
        {
            TryDeleteFile(archivePath);
            return InstallOutcome.Failure($"The download from the official site failed: {exception.Message} Nothing was installed.");
        }

        try
        {
            // ---- 2. verify checksum BEFORE anything else is touched
            progress?.Report(new InstallProgress(InstallProgress.StageVerifying, 72));
            if (!string.Equals(checksum, download.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return InstallOutcome.Failure(
                    $"Checksum mismatch for {entry.Name}: the downloaded file does not match the expected SHA-256 " +
                    $"({ShortHash(download.Sha256)}). It was deleted and nothing was installed — try again from Settings. " +
                    "(This protects against corrupted or tampered downloads.)");
            }

            // ---- 3. extract into a temp dir, validate the executable, then move into place
            progress?.Report(new InstallProgress(InstallProgress.StageExtracting, 74));
            var tempDir = Path.Combine(_rootDir, $".tmp-{entry.Id}-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(tempDir);
                var extracted = EmulatorArchive.Extract(archivePath, download.Format, tempDir, cancellationToken);
                progress?.Report(new InstallProgress(InstallProgress.StageExtracting, 88, $"{extracted.FileCount} files"));
                var extractedExecutable = EmulatorArchive.ResolveExecutable(
                    tempDir, download.ExecutablePathInArchive, extracted.FlattenedRootFolder, out var problem);
                if (extractedExecutable is null)
                {
                    return InstallOutcome.Failure(problem ?? "The downloaded archive could not be verified.");
                }

                await Task.Run(
                    () => MoveExtractedIntoPlace(tempDir, installDir), cancellationToken).ConfigureAwait(false);
                var finalExecutable = ReplacePrefix(extractedExecutable, tempDir, installDir);

                // ---- 4. record provenance + wire up the existing emulator_configs rows
                progress?.Report(new InstallProgress(InstallProgress.StageConfiguring, 92));
                var record = new EmulatorDownloadRecord
                {
                    EmulatorId = entry.Id,
                    Version = entry.Version,
                    Sha256 = download.Sha256,
                    SourceUrl = download.Url,
                    InstallFolder = installDir,
                    ExecutablePath = finalExecutable,
                    DownloadedAt = DateTime.UtcNow,
                };
                _database.SaveEmulatorDownload(record);
                foreach (var systemKey in entry.Systems)
                {
                    _database.SaveEmulatorConfig(systemKey, finalExecutable, download.LaunchArguments);
                }
                if (_database.LoadError is { } databaseError)
                {
                    return InstallOutcome.Failure(
                        "The emulator was placed in " + installDir + " but the database could not be updated, so it is " +
                        "not wired up yet. Try the download again from Settings. Error: " + databaseError);
                }

                progress?.Report(new InstallProgress(InstallProgress.StageDone, 100, finalExecutable));
                return new InstallOutcome { Success = true, ExecutablePath = finalExecutable };
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }
    /// <summary>Download phase helper: streams to disk while hashing, with throttled progress.</summary>
    private async Task<string> DownloadAndHashAsync(
        string url,
        string destinationPart,
        long? totalBytes,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var reportedTotal = totalBytes;
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 0)
        {
            reportedTotal = contentLength;
        }
        long offset = 0;
        using var incrementalHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var lastReport = DateTime.UtcNow;
        await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var fileStream = new FileStream(destinationPart, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024))
        {
            var buffer = new byte[512 * 1024];
            int read;
            while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                incrementalHash.AppendData(buffer, 0, read);
                offset += read;
                var now = DateTime.UtcNow;
                if (totalBytes is not null && (now - lastReport).TotalMilliseconds > 120)
                {
                    lastReport = now;
                    var percent = 70.0 * Math.Min(1.0, (double)offset / reportedTotal!.Value);
                    progress?.Report(new InstallProgress(InstallProgress.StageDownloading, percent,
                        $"{FormatMegabytes(offset)} of {FormatMegabytes(reportedTotal.Value)}"));
                }
            }
        }
        return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Moves the validated temp dir to its final install path (same volume).</summary>
    private static void MoveExtractedIntoPlace(string tempDir, string installDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installDir)!);
        if (Directory.Exists(installDir))
        {
            // App-managed install folder under our Emulators root: a matching name means a
            // previous install of the same version (or a rewrite of a broken one), and it
            // is replaced wholesale. Nothing outside Emulators\<system>\<name>-<version>
            // is ever written or deleted.
            Directory.Delete(installDir, recursive: true);
        }
        try
        {
            Directory.Move(tempDir, installDir);
        }
        catch (IOException)
        {
            // Antivirus/OneDrive handles can briefly hold the rename; one short retry
            // keeps installs reliable without masking real failures.
            Thread.Sleep(300);
            Directory.Move(tempDir, installDir);
        }
    }

    // ------------------------------------------------------------ removal

    /// <summary>
    /// Removes a downloaded install for the emulator serving this system key: clears
    /// emulator_configs rows that point inside the install folder (leaving any manual
    /// configuration untouched), deletes the emulator_downloads record, and deletes the
    /// install folder itself. Returns a user-facing message either way.
    /// </summary>
    public string RemoveInstall(string systemKey)
    {
        var entry = GetEntryForSystem(systemKey);
        if (entry is null)
        {
            return "No official build is offered for this system.";
        }
        var record = GetRecordForEmulatorId(entry.Id);
        if (record is null)
        {
            return $"{entry.Name} is not installed through PokeLauncher.";
        }
        if (_inProgressIds.Contains(entry.Id))
        {
            return "Cannot remove the emulator while it is downloading.";
        }

        var problems = new List<string>();
        var installRoot = Path.GetFullPath(record.InstallFolder);
        var root = Path.GetFullPath(_rootDir) + Path.DirectorySeparatorChar;
        if (Directory.Exists(installRoot) && installRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.Delete(installRoot, recursive: true);
            }
            catch (Exception exception)
            {
                problems.Add($"The files could not be fully deleted (possibly still in use): {exception.Message}");
            }
        }

        foreach (var covered in entry.Systems)
        {
            var config = _database.LoadEmulatorConfigs().TryGetValue(covered, out var value) ? value : null;
            if (config is not null && IsInsideInstallFolder(config.ExecutablePath, installRoot))
            {
                _database.SaveEmulatorConfig(covered, string.Empty, string.Empty);
            }
        }
        _database.DeleteEmulatorDownload(entry.Id);

        return problems.Count == 0
            ? $"{entry.Name} {record.Version} was removed. The install folder and its configuration were cleared; your own copies elsewhere were not touched."
            : $"{entry.Name} was removed from PokeLauncher's records, but the files may remain in {record.InstallFolder}. {problems[0]}";
    }

    private static bool IsInsideInstallFolder(string executablePath, string installRoot)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }
        var full = Path.GetFullPath(executablePath);
        return full.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ cleanup + helpers

    /// <summary>Deletes leftover .part downloads and aborted .tmp folders from earlier runs.</summary>
    private void CleanupStaleTemp()
    {
        foreach (var file in Directory.GetFiles(_downloadsDir, "*.part"))
        {
            TryDeleteFile(file);
        }
        foreach (var dir in Directory.GetDirectories(_rootDir, ".tmp-*"))
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            /* best effort; the next run's CleanupStaleTemp will retry */
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            /* best effort */
        }
    }

    /// <summary>Folder names derive from the manifest system key and "<name>-<version>"; keep them tame.</summary>
    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "_" : cleaned;
    }

    private static string ReplacePrefix(string path, string oldPrefix, string newPrefix)
    {
        var relative = Path.GetRelativePath(oldPrefix, path);
        return Path.GetFullPath(Path.Combine(newPrefix, relative));
    }

    private static string ShortHash(string hash) =>
        hash.Length <= 12 ? hash : hash[..12];

    private static string FormatMegabytes(long bytes) =>
        (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
}
