namespace PokeLauncher.Models;

/// <summary>One row of the Stats page's most-played list.</summary>
public sealed record PlaytimeByGameRow(
    string GameId,
    string Title,
    double TotalSeconds,
    long Sessions);
