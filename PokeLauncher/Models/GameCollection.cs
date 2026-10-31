using System.Collections.Generic;

namespace PokeLauncher.Models;

/// <summary>
/// A named grouping of catalog game ids (e.g. "Kanto Classics").
/// </summary>
public sealed class GameCollection
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<string> GameIds { get; init; }
}
