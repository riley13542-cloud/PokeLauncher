using System;
using System.Collections.Generic;

namespace PokeLauncher.Models;

/// <summary>
/// A user-added ROM hack entry. The hack's playable file is an already-patched ROM the
/// user provides (PokeLauncher does not patch); it is launched through the base game's
/// platform emulator, exactly like a built-in game.
/// </summary>
public sealed class RomHack
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Id of the built-in catalog game this hack is based on (provides the platform).</summary>
    public required string BaseGameId { get; init; }

    public required string Description { get; init; }

    public required string Creator { get; init; }

    /// <summary>Optional link to the hack's official page.</summary>
    public string? ExternalUrl { get; init; }

    /// <summary>Absolute path of the patched ROM file, or null when not provided yet.</summary>
    public string? RomPath { get; init; }

    public required string Genre { get; init; }

    /// <summary>One of: Easy, Medium, Hard, Very Hard.</summary>
    public required string Difficulty { get; init; }

    /// <summary>One of: Complete, Beta, In Development, Discontinued.</summary>
    public required string Status { get; init; }

    public DateTime? CreatedAt { get; init; }

    public bool IsBookmarked { get; init; }

    /// <summary>Convenience copy of the fields the details page shows, kept out of the DB.</summary>
    public const string DefaultGenre = "Exploration";
    public const string DefaultDifficulty = "Medium";
    public const string DefaultStatus = "In Development";
}
