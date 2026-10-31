using System;

namespace PokeLauncher.Models;

/// <summary>
/// Catalog entry for an official Pokémon game. Catalog rows are seeded into the local
/// database on first run; favorite/play state lives alongside them. A row's existence
/// does not imply that a ROM file exists or is bundled — check <see cref="RomPath"/>.
/// </summary>
public sealed class PokemonGame
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required int Generation { get; init; }

    public required string Platform { get; init; }

    public required int ReleaseYear { get; init; }

    public required string Description { get; init; }

    /// <summary>Placeholder cover art gradient start color (hex).</summary>
    public required string CoverFromHex { get; init; }

    /// <summary>Placeholder cover art gradient end color (hex).</summary>
    public required string CoverToHex { get; init; }

    /// <summary>
    /// True when this row was created by importing a user-provided ROM file rather than
    /// coming from the built-in seed catalog. Imported games can be removed from the
    /// library; catalog entries stay (only their ROM link or metadata may change).
    /// </summary>
    public bool IsImported { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>Absolute path of the user's ROM file, or null when not imported.</summary>
    public string? RomPath { get; init; }

    public DateTime? LastPlayed { get; init; }

    public double PlaytimeHours { get; init; }

    /// <summary>When the user imported this game's ROM (null for seeded catalog entries).</summary>
    public DateTime? ImportedAt { get; init; }

    /// <summary>
    /// Creates a copy of this entry with user-edited metadata applied, keeping identity,
    /// play state, and cover colors unchanged.
    /// </summary>
    public PokemonGame WithMetadata(string title, string platform, int generation, string description) => new()
    {
        Id = Id,
        Title = title,
        Generation = generation,
        Platform = platform,
        ReleaseYear = ReleaseYear,
        Description = description,
        CoverFromHex = CoverFromHex,
        CoverToHex = CoverToHex,
        IsFavorite = IsFavorite,
        RomPath = RomPath,
        LastPlayed = LastPlayed,
        PlaytimeHours = PlaytimeHours,
        IsImported = IsImported,
        ImportedAt = ImportedAt,
    };

    /// <summary>Creates a copy of this entry pointing at a different ROM file.</summary>
    public PokemonGame WithRomPath(string romPath) => new()
    {
        Id = Id,
        Title = Title,
        Generation = Generation,
        Platform = Platform,
        ReleaseYear = ReleaseYear,
        Description = Description,
        CoverFromHex = CoverFromHex,
        CoverToHex = CoverToHex,
        IsFavorite = IsFavorite,
        RomPath = romPath,
        LastPlayed = LastPlayed,
        PlaytimeHours = PlaytimeHours,
        IsImported = IsImported,
        ImportedAt = ImportedAt,
    };
}
