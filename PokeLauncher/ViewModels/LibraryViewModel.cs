using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PokeLauncher.ViewModels;

public enum LibrarySort
{
    Title,
    TitleDescending,
    Generation,
    RecentlyPlayed,
    Playtime,
}

public sealed class LibrarySortOption
{
    public LibrarySortOption(string label, LibrarySort mode)
    {
        Label = label;
        Mode = mode;
    }

    public string Label { get; }

    public LibrarySort Mode { get; }
}

public sealed partial class LibraryViewModel : PageViewModelBase, ISearchablePage, IRefreshablePage
{
    private readonly List<GameViewModel> _allGames;
    private readonly Func<Task> _requestImport;
    private readonly Func<Task> _checkInbox;
    private readonly Func<Task> _requestFolderImport;

    public LibraryViewModel(IReadOnlyList<GameViewModel> allGames, Func<Task> requestImport, Func<Task> checkInbox, Func<Task> requestFolderImport)
    {
        _allGames = allGames.ToList();
        _requestImport = requestImport;
        _checkInbox = checkInbox;
        _requestFolderImport = requestFolderImport;
        foreach (var game in _allGames)
        {
            game.PropertyChanged += OnGamePropertyChanged;
        }
        _selectedGeneration = FilterOptions.Generations[0];
        _selectedPlatform = "All platforms";
        _selectedSort = SortOptions[0];
        Recalculate();
    }

    public override string DisplayName => "My Library";

    public override string IconGlyph => "\uE8F1";

    public override string? HeaderSubtitle => "Browse, filter, and organize your games";

    public ObservableCollection<GameViewModel> FilteredGames { get; } = new();

    public int TotalCount => _allGames.Count;

    public int ShownCount => FilteredGames.Count;

    public bool HasResults => FilteredGames.Count > 0;

    public string CountLabel => $"{ShownCount} of {TotalCount} games";

    public IReadOnlyList<LibrarySortOption> SortOptions { get; } = new LibrarySortOption[]
    {
        new("Title (A–Z)", LibrarySort.Title),
        new("Title (Z–A)", LibrarySort.TitleDescending),
        new("Generation", LibrarySort.Generation),
        new("Recently played", LibrarySort.RecentlyPlayed),
        new("Playtime", LibrarySort.Playtime),
    };

    public IReadOnlyList<GenOption> GenerationOptions => FilterOptions.Generations;

    public IReadOnlyList<string> PlatformOptions { get; } = new[]
    {
        "All platforms", "Game Boy", "Game Boy Color", "Game Boy Advance", "Nintendo DS",
    };

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private GenOption? _selectedGeneration;

    [ObservableProperty]
    private string? _selectedPlatform;

    [ObservableProperty]
    private LibrarySortOption? _selectedSort;

    [ObservableProperty]
    private bool _favoritesOnly;

    [ObservableProperty]
    private bool _missingRomsOnly;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public bool HasStatusMessage => StatusMessage is not null;

    public string? SearchQuery
    {
        get => SearchText;
        set => SearchText = value;
    }

    public void Refresh() => Recalculate();

    /// <summary>Adds freshly imported games to the library and keeps the filters in sync.</summary>
    public void AddGames(IEnumerable<GameViewModel> games)
    {
        foreach (var game in games)
        {
            if (_allGames.Any(existing => existing.Id == game.Id))
            {
                continue;
            }
            game.PropertyChanged += OnGamePropertyChanged;
            _allGames.Add(game);
        }
        Recalculate();
    }

    /// <summary>Drops a removed game from the library.</summary>
    public void RemoveGame(GameViewModel game)
    {
        game.PropertyChanged -= OnGamePropertyChanged;
        _allGames.Remove(game);
        Recalculate();
    }

    partial void OnSearchTextChanged(string? value) => Recalculate();

    partial void OnSelectedGenerationChanged(GenOption? value) => Recalculate();

    partial void OnSelectedPlatformChanged(string? value) => Recalculate();

    partial void OnSelectedSortChanged(LibrarySortOption? value) => Recalculate();

    partial void OnFavoritesOnlyChanged(bool value) => Recalculate();

    partial void OnMissingRomsOnlyChanged(bool value) => Recalculate();

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep the favorites filter and live counts in sync with shared game state.
        if (e.PropertyName == nameof(GameViewModel.IsFavorite))
        {
            Recalculate();
        }
    }

    private void Recalculate()
    {
        var query = SearchText?.Trim();
        IEnumerable<GameViewModel> games = _allGames;

        if (!string.IsNullOrEmpty(query))
        {
            games = games.Where(g => g.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (SelectedGeneration?.Value is { } generation)
        {
            games = games.Where(g => g.Generation == generation);
        }
        if (!string.IsNullOrEmpty(SelectedPlatform) && SelectedPlatform != "All platforms")
        {
            games = games.Where(g => g.Platform == SelectedPlatform);
        }
        if (FavoritesOnly)
        {
            games = games.Where(g => g.IsFavorite);
        }
        if (MissingRomsOnly)
        {
            games = games.Where(g => !g.HasHealthyRom);
        }

        games = SelectedSort?.Mode switch
        {
            LibrarySort.Generation => games.OrderBy(g => g.Generation).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase),
            LibrarySort.RecentlyPlayed => games.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue),
            LibrarySort.Playtime => games.OrderByDescending(g => g.PlaytimeHours),
            LibrarySort.TitleDescending => games.OrderByDescending(g => g.Title, StringComparer.OrdinalIgnoreCase),
            _ => games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase),
        };

        FilteredGames.Clear();
        foreach (var game in games)
        {
            FilteredGames.Add(game);
        }
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CountLabel));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedGeneration = FilterOptions.Generations[0];
        SelectedPlatform = "All platforms";
        SelectedSort = SortOptions[0];
        FavoritesOnly = false;
        MissingRomsOnly = false;
        StatusMessage = null;
    }

    [RelayCommand]
    private Task ImportGames() => _requestImport();

    [RelayCommand]
    private Task CheckInbox() => _checkInbox();

    [RelayCommand]
    private Task ImportFromFolder() => _requestFolderImport();

    [RelayCommand]
    private void ClearStatus() => StatusMessage = null;
}
