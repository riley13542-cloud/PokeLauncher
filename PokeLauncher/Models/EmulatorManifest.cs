using System.Collections.Generic;

namespace PokeLauncher.Models;

/// <summary>
/// The emulator download manifest (emulators.json). Describes what PokeLauncher can
/// download on demand: one entry per downloadable build, each pinning an exact
/// official-source URL, SHA-256 checksum, and archive layout. The manifest is bundled
/// with the app and may be replaced without an app update by dropping an updated
/// emulators.json into %LocalAppData%\PokeLauncher.
///
/// Downloading is always consent-driven from the Settings page; nothing is fetched,
/// extracted, or configured until the user confirms it.
/// </summary>
public sealed class EmulatorManifest
{
    public int SchemaVersion { get; init; }

    public IReadOnlyList<EmulatorManifestEntry> Emulators { get; init; } =
        new List<EmulatorManifestEntry>();
}

/// <summary>One downloadable emulator build (e.g. mGBA 0.10.5 win64).</summary>
public sealed class EmulatorManifestEntry
{
    /// <summary>Stable identifier used as the emulator_downloads table key. Lowercase, e.g. "mgba".</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    /// <summary>Emulator system keys this build can run (e.g. GB + GBC + GBA for mGBA).</summary>
    public required IReadOnlyList<string> Systems { get; init; }

    public string? Description { get; init; }

    /// <summary>Short license identifier shown to the user, e.g. "MPL-2.0".</summary>
    public required string License { get; init; }

    public required string LicenseUrl { get; init; }

    /// <summary>The project's own repository or release page — never a mirror or ROM site.</summary>
    public required string SourceUrl { get; init; }

    public string? HomepageUrl { get; init; }

    /// <summary>Honest user-facing caveat, e.g. BIOS/firmware requirements. Null when none.</summary>
    public string? Note { get; init; }

    public required EmulatorManifestDownload Download { get; init; }
}

/// <summary>Exact download payload: official URL, checksum, and archive layout.</summary>
public sealed class EmulatorManifestDownload
{
    public required string Url { get; init; }

    /// <summary>Hex SHA-256 of the archive, case-insensitive.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Compressed archive size in bytes; used for download progress. 0 when unknown.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Container format: "zip" or "7z".</summary>
    public required string Format { get; init; }

    /// <summary>
    /// Path of the launcher executable inside the archive, using '/' separators exactly
    /// as stored in the archive (e.g. "mGBA-0.10.5-win64/mGBA.exe").
    /// </summary>
    public required string ExecutablePathInArchive { get; init; }

    /// <summary>
    /// Default launch arguments written into emulator_configs after install.
    /// Supports the {rom} placeholder; the future launcher substitutes and quotes it.
    /// </summary>
    public string LaunchArguments { get; init; } = "{rom}";
}
