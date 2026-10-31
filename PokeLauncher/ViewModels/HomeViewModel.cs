using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace PokeLauncher.ViewModels;

public sealed partial class HomeViewModel : PageViewModelBase
{
    private readonly List<GameViewModel> _games;
    private readonly ObservableCollection<RomHackViewModel> _hacks;
    private readonly LibraryStatsViewModel _stats;
    private readonly Action<GameViewModel> _openGame;
    private readonly Action _navigateToLibrary;
    private readonly Action _navigateToHacks;
    private readonly Func<GameViewModel, Task> _playGame;

    public HomeViewModel(
        IReadOnlyList<GameViewModel> games,
        IReadOnlyList<RomHackViewModel> hacks,
        LibraryStatsViewModel stats,
        Action<GameViewModel> openGame,
        Action navigateToLibrary,
        Action navigateToHacks,
        Func<GameViewModel, Task> playGame)
    {
        _games = games.ToList();
        _hacks = hacks is ObservableCollection<RomHackViewModel> oc ? oc : new ObservableCollection<RomHackViewModel>(hacks);
        _stats = stats;
        _openGame = openGame;
        _navigateToLibrary = navigateToLibrary;
        _navigateToHacks = navigateToHacks;
        _playGame = playGame;

        FeaturedGame = _games.First(g => g.Id == "emerald");
        RefreshPlayData();
        RefreshFeaturedHacks();
        _hacks.CollectionChanged += (_, _) => RefreshFeaturedHacks();
        foreach (var hack in _hacks)
        {
            hack.PropertyChanged += (_, _) => RefreshFeaturedHacks();
        }
    }

    public override string DisplayName => "Home";

    public override string IconGlyph => "\uE80F";

    public override string? HeaderSubtitle => "Your Pokémon collection at a glance";

    public GameViewModel FeaturedGame { get; }

    public IReadOnlyList<GameViewModel> ContinuePlaying { get; private set; } =
        Array.Empty<GameViewModel>();

    public IReadOnlyList<GameViewModel> FavoriteGames { get; private set; } =
        Array.Empty<GameViewModel>();

    public IReadOnlyList<RomHackViewModel> FeaturedHacks { get; private set; } =
        Array.Empty<RomHackViewModel>();

    public bool HasFeaturedHacks => FeaturedHacks.Count > 0;

    /// <summary>Rebuilds the Discover ROM Hacks row from the live hack list.</summary>
    public void RefreshFeaturedHacks()
    {
        FeaturedHacks = _hacks
            .OrderByDescending(h => h.IsBookmarked)
            .ThenByDescending(h => h.CreatedAt ?? DateTime.MinValue)
            .Take(3)
            .ToList();
        OnPropertyChanged(nameof(FeaturedHacks));
        OnPropertyChanged(nameof(HasFeaturedHacks));
    }

    public LibraryStatsViewModel Stats => _stats;

    public bool HasContinuePlaying => ContinuePlaying.Count > 0;

    public bool HasFavorites => FavoriteGames.Count > 0;

    /// <summary>
    /// Rebuilds the "Continue Playing" and "Favorites" rows from live game state; called
    /// when play state changes and after imports/removals so Home stays current.
    /// </summary>
    public void RefreshPlayData()
    {
        ContinuePlaying = _games
            .Where(g => g.IsPlayed)
            .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
            .Take(5)
            .ToList();
        FavoriteGames = _games.Where(g => g.IsFavorite).ToList();
        OnPropertyChanged(nameof(ContinuePlaying));
        OnPropertyChanged(nameof(FavoriteGames));
        OnPropertyChanged(nameof(HasContinuePlaying));
        OnPropertyChanged(nameof(HasFavorites));
    }

    /// <summary>Includes newly imported games on the dashboard.</summary>
    public void AddGames(IEnumerable<GameViewModel> games)
    {
        foreach (var game in games)
        {
            if (_games.All(existing => existing.Id != game.Id))
            {
                _games.Add(game);
            }
        }
        RefreshPlayData();
    }

    /// <summary>Drops a removed game from the dashboard.</summary>
    public void RemoveGame(GameViewModel game)
    {
        _games.Remove(game);
        RefreshPlayData();
    }

    [RelayCommand]
    private Task PlayFeatured() => _playGame(FeaturedGame);

    [RelayCommand]
    private void ViewFeaturedDetails() => _openGame(FeaturedGame);

    [RelayCommand]
    private void OpenLibrary() => _navigateToLibrary();

    [RelayCommand]
    private void OpenHackBrowser() => _navigateToHacks();
}
