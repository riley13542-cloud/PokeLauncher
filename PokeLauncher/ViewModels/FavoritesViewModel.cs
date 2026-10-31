using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PokeLauncher.ViewModels;

public sealed partial class FavoritesViewModel : PageViewModelBase, ISearchablePage, IRefreshablePage
{
    private readonly List<GameViewModel> _allGames;

    public FavoritesViewModel(IReadOnlyList<GameViewModel> allGames)
    {
        _allGames = allGames.ToList();
        foreach (var game in _allGames)
        {
            game.PropertyChanged += OnGamePropertyChanged;
        }
        Recalculate();
    }

    public override string DisplayName => "Favorites";

    public override string IconGlyph => "\uE735";

    public override string? HeaderSubtitle => "Games and bookmarks you starred";

    public ObservableCollection<GameViewModel> FavoriteGames { get; } = new();

    public int Count => FavoriteGames.Count;

    public bool HasFavorites => FavoriteGames.Count > 0;

    public string CountLabel => $"{FavoriteGames.Count} {(FavoriteGames.Count == 1 ? "favorite" : "favorites")}";

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    public string? SearchQuery
    {
        get => SearchText;
        set => SearchText = value;
    }

    public void Refresh() => Recalculate();

    /// <summary>Adds newly imported games so favoriting them shows up here.</summary>
    public void AddGames(IEnumerable<GameViewModel> games)
    {
        foreach (var game in games)
        {
            if (_allGames.All(existing => existing.Id != game.Id))
            {
                game.PropertyChanged += OnGamePropertyChanged;
                _allGames.Add(game);
            }
        }
        Recalculate();
    }

    /// <summary>Drops a removed game.</summary>
    public void RemoveGame(GameViewModel game)
    {
        game.PropertyChanged -= OnGamePropertyChanged;
        _allGames.Remove(game);
        Recalculate();
    }

    partial void OnSearchTextChanged(string? value) => Recalculate();

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.IsFavorite))
        {
            Recalculate();
        }
    }

    private void Recalculate()
    {
        var query = SearchText?.Trim();
        var favorites = _allGames
            .Where(g => g.IsFavorite)
            .Where(g => string.IsNullOrEmpty(query) || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FavoriteGames.Clear();
        foreach (var game in favorites)
        {
            FavoriteGames.Add(game);
        }
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(CountLabel));
    }
}
