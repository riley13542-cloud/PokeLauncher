using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PokeLauncher.ViewModels;

/// <summary>
/// Aggregated library statistics shown on the Home dashboard. Recomputes when favorite
/// or play states change on the shared game view models, and when games are imported
/// or removed.
/// </summary>
public class LibraryStatsViewModel : ViewModelBase
{
    private readonly List<GameViewModel> _games;

    public LibraryStatsViewModel(IReadOnlyList<GameViewModel> games, IReadOnlyList<RomHackViewModel> hacks)
    {
        _games = games.ToList();
        HackCount = hacks.Count;
        foreach (var game in _games)
        {
            game.PropertyChanged += OnGamePropertyChanged;
        }
        Refresh();
    }

    public int TotalGames { get; private set; }

    public int FavoriteCount { get; private set; }

    public int PlayedCount { get; private set; }

    public int HackCount { get; private set; }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.IsFavorite)
            or nameof(GameViewModel.LastPlayed))
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        TotalGames = _games.Count;
        FavoriteCount = _games.Count(static g => g.IsFavorite);
        PlayedCount = _games.Count(static g => g.IsPlayed);
        OnPropertyChanged(nameof(TotalGames));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(PlayedCount));
        OnPropertyChanged(nameof(HackCount));
    }

    /// <summary>Includes newly imported games in the counts.</summary>
    public void AddGames(IEnumerable<GameViewModel> games)
    {
        foreach (var game in games)
        {
            if (_games.All(existing => existing.Id != game.Id))
            {
                game.PropertyChanged += OnGamePropertyChanged;
                _games.Add(game);
            }
        }
        Refresh();
    }

    /// <summary>Drops a removed game from the counts.</summary>
    public void RemoveGame(GameViewModel game)
    {
        game.PropertyChanged -= OnGamePropertyChanged;
        _games.Remove(game);
        Refresh();
    }
}
