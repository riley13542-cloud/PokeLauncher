using System;

namespace PokeLauncher.Models;

/// <summary>
/// A completed emulator install recorded in the emulator_downloads table: what was
/// downloaded (version + checksum + source URL), where it was extracted, and the
/// executable PokeLauncher configured. Kept separate from emulator_configs so the
/// config table stays a plain "what do we launch" row while this table records the
/// download provenance needed for updates and clean removal.
/// </summary>
public sealed class EmulatorDownloadRecord
{
    /// <summary>Manifest entry id, e.g. "mgba". One install covers every system it supports.</summary>
    public required string EmulatorId { get; init; }

    public required string Version { get; init; }

    /// <summary>Verified checksum of the archive that was extracted here.</summary>
    public required string Sha256 { get; init; }

    public required string SourceUrl { get; init; }

    /// <summary>Folder the archive was extracted into, under ...\PokeLauncher\Emulators\.</summary>
    public required string InstallFolder { get; init; }

    /// <summary>Absolute path of the emulator executable that was configured.</summary>
    public required string ExecutablePath { get; init; }

    public required DateTime DownloadedAt { get; init; }
}
