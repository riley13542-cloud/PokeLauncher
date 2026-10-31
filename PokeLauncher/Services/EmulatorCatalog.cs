using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PokeLauncher.Models;

namespace PokeLauncher.Services;

/// <summary>Outcome of loading the emulator manifest: a manifest (possibly with per-entry skips) or fatal errors.</summary>
public sealed class EmulatorManifestLoadResult
{
    public static readonly EmulatorManifestLoadResult Empty =
        new(null, new List<string>(), false);

    public EmulatorManifestLoadResult(
        EmulatorManifest? manifest,
        IReadOnlyList<string> errors,
        bool usedOverride)
    {
        Manifest = manifest;
        Errors = errors;
        UsedOverride = usedOverride;
    }

    public EmulatorManifest? Manifest { get; }

    /// <summary>Fatal loader errors and per-entry validation failures, user-displayable.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when a valid override emulators.json was used instead of the bundled one.</summary>
    public bool UsedOverride { get; }
}

/// <summary>
/// Loads and validates the emulator manifest (emulators.json). Source of truth is the
/// manifest bundled with the app; an optional file at %LocalAppData%\PokeLauncher\emulators.json
/// (or an injected directory, for tests) lets the user or a future update channel replace
/// entries without shipping a new app build. The override is only used when it parses and
/// validates cleanly — otherwise the bundled manifest stays authoritative and the problems
/// are surfaced to the UI.
/// </summary>
public sealed class EmulatorCatalog
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Pinned manifest: match property names exactly so typo'd JSON fails loudly.
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static readonly Lazy<string> _lazyEmbeddedJson =
        new(ReadEmbeddedManifest, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly string? _overrideDirectory;

    public EmulatorCatalog(string? overrideDirectory = null)
    {
        _overrideDirectory = overrideDirectory;
    }

    /// <summary>Directory holding the optional override emulators.json.</summary>
    public static string DefaultOverrideDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLauncher");

    private static string OverrideFileName => "emulators.json";

    private string? OverrideFilePath =>
        _overrideDirectory is null ? null : Path.Combine(_overrideDirectory, OverrideFileName);

    public string EmbeddedResourceName => "PokeLauncher.emulators.json";

    public EmulatorManifestLoadResult Load()
    {
        var errors = new List<string>();

        // 1) Optional user/update-channel override, used only when fully valid.
        if (OverrideFilePath is { } path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var result = Parse(json, $"override file {path}");
                    if (result.Manifest is not null)
                    {
                        return new EmulatorManifestLoadResult(result.Manifest, result.Errors, true);
                    }
                    errors.AddRange(result.Errors);
                    errors.Add("Falling back to the emulator manifest bundled with PokeLauncher.");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"Could not read the override emulator manifest at {path}: {exception.Message}");
                errors.Add("Falling back to the emulator manifest bundled with PokeLauncher.");
            }
        }

        // 2) Bundled manifest — always present in a correctly built app.
        try
        {
            var result = Parse(_lazyEmbeddedJson.Value, "bundled manifest");
            errors.AddRange(result.Errors);
            if (result.Manifest is not null)
            {
                return new EmulatorManifestLoadResult(result.Manifest, errors, false);
            }
            return new EmulatorManifestLoadResult(null, errors, false);
        }
        catch (Exception exception)
        {
            errors.Add($"Could not read the bundled emulator manifest: {exception.Message}");
            return new EmulatorManifestLoadResult(null, errors, false);
        }
    }

    /// <summary>Parses + validates manifest JSON; invalid entries are skipped with errors, not fatal.</summary>
    public EmulatorManifestLoadResult Parse(string json, string originLabel)
    {
        var errors = new List<string>();
        EmulatorManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EmulatorManifest>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            return new EmulatorManifestLoadResult(null, new[] { $"Emulator manifest ({originLabel}) is not valid JSON: {exception.Message}" }, false);
        }

        if (manifest is null)
        {
            return new EmulatorManifestLoadResult(
                null,
                new[] { $"Emulator manifest ({originLabel}) is empty." },
                false);
        }

        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            return new EmulatorManifestLoadResult(
                null,
                new[]
                {
                    manifest.SchemaVersion < SupportedSchemaVersion
                        ? $"Emulator manifest ({originLabel}) uses schema version {manifest.SchemaVersion}, which is too old for this build of PokeLauncher."
                        : $"Emulator manifest ({originLabel}) uses schema version {manifest.SchemaVersion}; this build of PokeLauncher understands version {SupportedSchemaVersion}. Update PokeLauncher.",
                },
                false);
        }

        var knownSystems = EmulatorSystems.All.Select(s => s.Key).ToHashSet();
        var valid = new List<EmulatorManifestEntry>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in manifest.Emulators ?? new List<EmulatorManifestEntry>())
        {
            var problems = GetEntryProblems(entry, knownSystems, seenIds);
            if (problems.Count == 0)
            {
                seenIds.Add(entry.Id);
                valid.Add(entry);
            }
            else
            {
                errors.AddRange(problems.Select(p => $"Ignoring '{entry.Id ?? "(missing id)"}' from {originLabel}: {p}"));
            }
        }

        if (valid.Count == 0)
        {
            errors.Insert(0, $"No valid emulator entries in {originLabel}.");
            return new EmulatorManifestLoadResult(null, errors, false);
        }

        return new EmulatorManifestLoadResult(
            new EmulatorManifest { SchemaVersion = manifest.SchemaVersion, Emulators = valid },
            errors,
            false);
    }

    private static List<string> GetEntryProblems(
        EmulatorManifestEntry? entry,
        IReadOnlySet<string> knownSystems,
        HashSet<string> seenIds)
    {
        var problems = new List<string>();
        if (entry is null)
        {
            problems.Add("entry is null");
            return problems;
        }

        if (string.IsNullOrWhiteSpace(entry.Id) || !entry.Id.Equals(entry.Id.Trim(), StringComparison.Ordinal))
        {
            problems.Add("requires a non-empty, trimmed id");
        }
        else if (!seenIds.Add(entry.Id))
        {
            problems.Add($"duplicate id '{entry.Id}'");
        }
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            problems.Add("requires a name");
        }
        if (string.IsNullOrWhiteSpace(entry.Version))
        {
            problems.Add("requires a version");
        }
        if (entry.Systems is null || entry.Systems.Count == 0)
        {
            problems.Add("requires at least one supported system");
        }
        else if (entry.Systems.Any(s => s is null || !knownSystems.Contains(s)))
        {
            problems.Add("lists an unknown system key");
        }
        if (string.IsNullOrWhiteSpace(entry.License))
        {
            problems.Add("requires a license identifier");
        }
        if (entry.Download is null)
        {
            problems.Add("requires download details");
            return problems;
        }

        if (!IsHttps(entry.Download.Url))
        {
            problems.Add("requires an https download URL (official sources only)");
        }
        if (!IsHttps(entry.LicenseUrl))
        {
            problems.Add("requires an https license URL");
        }
        if (!IsHttps(entry.SourceUrl))
        {
            problems.Add("requires an https source URL");
        }
        if (string.IsNullOrWhiteSpace(entry.Download.Sha256) ||
            !entry.Download.Sha256.All(IsHexDigit) ||
            entry.Download.Sha256.Length != 64)
        {
            problems.Add("requires a 64-character hex SHA-256 checksum");
        }
        var format = entry.Download.Format?.Trim().ToLowerInvariant();
        if (format is not ("zip" or "7z"))
        {
            problems.Add("requires archive format 'zip' or '7z'");
        }
        if (string.IsNullOrWhiteSpace(entry.Download.ExecutablePathInArchive))
        {
            problems.Add("requires the executable path inside the archive");
        }
        else if (LooksUnsafePath(entry.Download.ExecutablePathInArchive))
        {
            problems.Add("executable path inside the archive must be relative and may not traverse up '..'");
        }

        return problems;

        static bool IsHttps(string? url) =>
            uriValid(url, out var uri) &&
            uri.Scheme is "https" &&
            !string.IsNullOrWhiteSpace(uri.Host);

        static bool uriValid(string? s, out Uri uri) => Uri.TryCreate(s, UriKind.Absolute, out uri!);

        static bool IsHexDigit(char c) =>
            c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

        static bool LooksUnsafePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return true;
            }
            foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".." || segment == ".")
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static string ReadEmbeddedManifest()
    {
        var assembly = typeof(EmulatorCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("PokeLauncher.emulators.json");
        if (stream is null)
        {
            throw new InvalidOperationException(
                "The bundled emulator manifest is missing from the app build (resource 'PokeLauncher.emulators.json' not found).");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Convenience lookup: the manifest entry that can run a given system key, or null.</summary>
    public static EmulatorManifestEntry? GetEntryForSystem(EmulatorManifest? manifest, string systemKey)
    {
        if (manifest is null || string.IsNullOrEmpty(systemKey))
        {
            return null;
        }
        var entry = manifest.Emulators.FirstOrDefault(e =>
            e.Systems.Count > 0 && e.Systems.Contains(systemKey, StringComparer.OrdinalIgnoreCase));
        return entry;
    }
}
