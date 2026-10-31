using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PokeLauncher.Services;

/// <summary>The update manifest served by whoever distributes PokeLauncher.</summary>
public sealed class UpdateManifest
{
    public string Version { get; init; } = string.Empty;

    /// <summary>Direct download URL of the installer for this version.</summary>
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>SHA-256 of the installer file — verified before it is ever run.</summary>
    public string Sha256 { get; init; } = string.Empty;

    public string? Notes { get; init; }
}

/// <summary>The result of an update check.</summary>
public sealed class UpdateCheckResult
{
    public bool Success { get; init; }

    /// <summary>True when the manifest reports a version newer than the running app.</summary>
    public bool UpdateAvailable { get; init; }

    public string Message { get; init; } = string.Empty;

    public UpdateManifest? Manifest { get; init; }
}

/// <summary>
/// Checks a version manifest and, with the user's consent, downloads + verifies the
/// update. The manifest is a small JSON file hosted by whoever distributes the app
/// (see UpdateManifestUrl); the downloaded installer is SHA-256-verified against the
/// manifest before it is ever run. Nothing is downloaded without the user's prompt.
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// Where the update manifest (update.json) is fetched from. Distributors replace
    /// this with their own URL (e.g. a GitHub Releases "latest" redirect or raw file).
    /// Empty disables update checks — the UI says so honestly.
    /// </summary>
    public const string DefaultManifestUrl = "https://github.com/riley13542-cloud/PokeLauncher/releases/latest/download/update.json";

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        // Test/distribution override without a rebuild.
        var env = Environment.GetEnvironmentVariable("POKELAUNCHER_UPDATE_MANIFEST");
        ManifestUrl = string.IsNullOrWhiteSpace(env) ? DefaultManifestUrl : env.Trim();
    }

    public string ManifestUrl { get; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ManifestUrl);

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Compares dotted versions numerically; "0.10.0" > "0.9.0".</summary>
    public static bool IsNewerVersion(string candidate, string current)
    {
        Version.TryParse(candidate.TrimStart('v', 'V'), out var candidateVersion);
        Version.TryParse(current.TrimStart('v', 'V'), out var currentVersion);
        if (candidateVersion is null || currentVersion is null)
        {
            return false;
        }
        return candidateVersion > currentVersion;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new UpdateCheckResult
            {
                Success = false,
                Message = "Update checks are not configured yet — this copy has no update source.",
            };
        }
        try
        {
            var json = await _httpClient.GetStringAsync(ManifestUrl, cancellationToken);
            var manifest = ParseManifest(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) ||
                string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                return new UpdateCheckResult { Success = false, Message = "The update manifest is invalid." };
            }
            if (!IsNewerVersion(manifest.Version, currentVersion))
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = false,
                    Manifest = manifest,
                    Message = $"You're up to date (version {currentVersion}).",
                };
            }
            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = true,
                Manifest = manifest,
                Message = $"Version {manifest.Version} is available.",
            };
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult
            {
                Success = false,
                Message = $"The update check failed: {exception.Message}",
            };
        }
    }

    /// <summary>Parses the manifest JSON (minimal, dependency-free).</summary>
    public static UpdateManifest? ParseManifest(string json)
    {
        try
        {
            json = json.TrimStart('\uFEFF');
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            return new UpdateManifest
            {
                Version = root.TryGetProperty("version", out var version) ? version.GetString() ?? string.Empty : string.Empty,
                DownloadUrl = root.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                Sha256 = root.TryGetProperty("sha256", out var sha) ? sha.GetString() ?? string.Empty : string.Empty,
                Notes = root.TryGetProperty("notes", out var notes) ? notes.GetString() : null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to a temp file and verifies its SHA-256 against the
    /// manifest. Throws on network failure; returns null (with the reason in
    /// <paramref name="error"/>) when the hash does not match — a mismatched file is
    /// deleted and never run.
    /// </summary>
    public async Task<string?> DownloadInstallerAsync(
        UpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(
            Path.GetTempPath(),
            "PokeLauncher-update",
            $"PokeLauncher-{manifest.Version}-setup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var response = await _httpClient.GetAsync(
            manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024))
        {
            var buffer = new byte[512 * 1024];
            long total = 0;
            long? length = response.Content.Headers.ContentLength;
            int read;
            while ((read = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                if (length is > 0)
                {
                    progress?.Report(Math.Min(100.0, 100.0 * total / length.Value));
                }
            }
        }

        var actual = await ComputeSha256Async(target, cancellationToken);
        if (!string.IsNullOrWhiteSpace(manifest.Sha256) &&
            !string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(target); } catch { }
            throw new InvalidOperationException(
                "The downloaded update failed its SHA-256 check and was deleted. Nothing was installed.");
        }
        return target;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var hasher = SHA256.Create();
        var hash = await hasher.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Runs the downloaded installer and returns true when it was started. The
    /// caller should close the app afterwards so the installer can replace files.</summary>
    public bool RunInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            return false;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        });
        return true;
    }

    /// <summary>Formats a file size for progress messages.</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.##} MB",
    };
}
