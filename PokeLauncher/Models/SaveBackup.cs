using System;
using System.Collections.Generic;

namespace PokeLauncher.Models;

/// <summary>One timestamped snapshot of a game's save files.</summary>
public sealed class SaveBackup
{
    /// <summary>Snapshot folder name (parseable timestamp), e.g. "2026-10-30_14-05-09".</summary>
    public required string Name { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required string Folder { get; init; }

    public required IReadOnlyList<string> Files { get; init; }

    public required long TotalBytes { get; init; }

    public string Summary => Files.Count == 1
        ? $"{Files.Count} file · {FormatSize(TotalBytes)}"
        : $"{Files.Count} files · {FormatSize(TotalBytes)}";

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.##} MB",
    };
}
