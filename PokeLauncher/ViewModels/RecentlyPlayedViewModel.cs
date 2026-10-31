using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PokeLauncher.ViewModels;

public sealed partial class RecentlyPlayedViewModel : PageViewModelBase, ISearchablePage, IRefreshablePage
{
    private readonly List<GameViewModel> _allGames;

    public RecentlyPlayedViewModel(IReadOnlyList<GameViewModel> allGames)
    {
        _allGames = allGames.ToList();
        Recalculate();
    }

    public override string DisplayName => "Recently Played";

    public override string IconGlyph => "\uE823";

    public override string? HeaderSubtitle => "Pick up where you left off";

    public ObservableCollection<GameViewModel> RecentGames { get; } = new();

    public int Count => RecentGames.Count;

    public bool HasRecentGames => RecentGames.Count > 0;

    public string CountLabel => $"{RecentGames.Count} {(RecentGames.Count == 1 ? "game" : "games")} played";

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    public string? SearchQuery
    {
        get => SearchText;
        set => SearchText = value;
    }

    public void Refresh() => Recalculate();

    /// <summary>Adds newly imported games so they can appear here once played.</summary>
    public void AddGames(IEnumerable<GameViewModel> games)
    {
        foreach (var game in games)
        {
            if (_allGames.All(existing => existing.Id != game.Id))
            {
                _allGames.Add(game);
            }
        }
        Recalculate();
    }

    /// <summary>Drops a removed game.</summary>
    public void RemoveGame(GameViewModel game)
    {
        _allGames.Remove(game);
        Recalculate();
    }

    partial void OnSearchTextChanged(string? value) => Recalculate();

    private void Recalculate()
    {
        var query = SearchText?.Trim();
        var recent = _allGames
            .Where(g => g.IsPlayed)
            .Where(g => string.IsNullOrEmpty(query) || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
            .ToList();

        RecentGames.Clear();
        foreach (var game in recent)
        {
            RecentGames.Add(game);
        }
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasRecentGames));
        OnPropertyChanged(nameof(CountLabel));
    }
}
