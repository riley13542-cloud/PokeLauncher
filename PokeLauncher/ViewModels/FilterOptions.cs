using System;
using System.Collections.Generic;

namespace PokeLauncher.ViewModels;

/// <summary>Generation filter option; Value is null for "all".</summary>
public sealed class GenOption
{
    public GenOption(int? value, string label)
    {
        Value = value;
        Label = label;
    }

    public int? Value { get; }

    public string Label { get; }
}

/// <summary>Shared filter option lists. The first entry of each list is the "all" sentinel.</summary>
public static class FilterOptions
{
    public static IReadOnlyList<GenOption> Generations { get; } = new GenOption[]
    {
        new(null, "All generations"),
        new(1, "Generation I"),
        new(2, "Generation II"),
        new(3, "Generation III"),
        new(4, "Generation IV"),
    };

    public static IReadOnlyList<string> Difficulties { get; } = new[]
    {
        "All difficulties", "Easy", "Medium", "Hard", "Very Hard",
    };

    public static IReadOnlyList<string> Statuses { get; } = new[]
    {
        "All statuses", "Complete", "Beta", "In Development", "Discontinued",
    };

    public static int DifficultyRank(string difficulty) => difficulty switch
    {
        "Easy" => 0,
        "Medium" => 1,
        "Hard" => 2,
        "Very Hard" => 3,
        _ => 4,
    };

    /// <summary>Roman numeral for a generation number (1–4), falling back to the plain number.</summary>
    public static string RomanLabel(int generation) => generation switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        _ => generation.ToString(),
    };
}
