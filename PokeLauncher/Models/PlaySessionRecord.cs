using System;

namespace PokeLauncher.Models;

/// <summary>
/// One recorded emulator play session. A row is written only after a real launch:
/// failed launches never create sessions. <see cref="DurationSeconds"/> is measured
/// from the moment the emulator process started to the moment it exited; the launcher
/// also adds the same duration to the game's total playtime, so this table is the
/// per-session history while games.playtime_hours stays the fast aggregate.
/// </summary>
public sealed class PlaySessionRecord
{
    /// <summary>Database row id; 0 until the record has been inserted.</summary>
    public long Id { get; init; }

    public required string GameId { get; init; }

    public required DateTime StartedAt { get; init; }

    public required DateTime EndedAt { get; init; }

    public required double DurationSeconds { get; init; }
}
