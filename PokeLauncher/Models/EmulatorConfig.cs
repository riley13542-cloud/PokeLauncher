using System.Collections.Generic;

namespace PokeLauncher.Models;

/// <summary>
/// A per-system emulator configuration. Each supported console has its own entry so users
/// can run different emulators for Game Boy, Game Boy Color, GBA, and DS titles.
/// Data is persisted in the emulator_configs table; empty paths mean "not configured".
/// </summary>
public sealed class EmulatorConfig
{
    public required string SystemKey { get; init; }

    /// <summary>Path to the emulator executable, or empty when not configured.</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Raw launch arguments. Supports a <c>{rom}</c> placeholder for the ROM path.</summary>
    public string Arguments { get; init; } = string.Empty;
}

/// <summary>
/// The emulator systems PokeLauncher supports in this phase. Keys are stable identifiers
/// stored in the database — never rename them without a data migration.
/// </summary>
public static class EmulatorSystems
{
    public static IReadOnlyList<EmulatorSystem> All { get; } = new EmulatorSystem[]
    {
        new("GB", "Game Boy", ".gb"),
        new("GBC", "Game Boy Color", ".gb, .gbc"),
        new("GBA", "Game Boy Advance", ".gba, .zip"),
        new("NDS", "Nintendo DS", ".nds"),
    };

    /// <summary>Maps a game's platform name to its emulator system key, or null for unknown platforms.</summary>
    public static string? SystemKeyForPlatform(string platform) => platform switch
    {
        "Game Boy" => "GB",
        "Game Boy Color" => "GBC",
        "Game Boy Advance" => "GBA",
        "Nintendo DS" => "NDS",
        _ => null,
    };
}

/// <summary>Static definition of an emulator system (shown in Settings).</summary>
public sealed record EmulatorSystem(string Key, string DisplayName, string FileHint);
