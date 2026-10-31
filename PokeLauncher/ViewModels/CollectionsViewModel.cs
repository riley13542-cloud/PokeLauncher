using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;

namespace PokeLauncher.ViewModels;

public sealed partial class CollectionsViewModel : PageViewModelBase, ISearchablePage, IRefreshablePage
{
    private readonly List<GameViewModel> _allGames;
    private readonly Func<Task> _requestCreate;
    private readonly Func<CollectionViewModel, Task> _requestEdit;

    public CollectionsViewModel(
        IReadOnlyList<GameCollection> collections,
        IReadOnlyList<GameViewModel> allGames,
        Func<Task> requestCreate,
        Func<CollectionViewModel, Task> requestEdit)
    {
        _allGames = allGames.ToList();
        _requestCreate = requestCreate;
        _requestEdit = requestEdit;
        foreach (var game in _allGames)
        {
            game.PropertyChanged += OnGamePropertyChanged;
        }
        var gamesById = _allGames.ToDictionary(g => g.Id, g => g);
        Collections = new ObservableCollection<CollectionViewModel>(collections.Select(def =>
            new CollectionViewModel(
                def.Id,
                def.Name,
                def.Description,
                def.GameIds.Where(gamesById.ContainsKey).Select(id => gamesById[id]).ToList(),
                SelectCollection)));
        _selectedCollection = Collections.FirstOrDefault();
        if (_selectedCollection is { } first)
        {
            first.IsSelected = true;
        }
        RefillVisibleGames();
    }

    public override string DisplayName => "Collections";

    public override string IconGlyph => "\uE71D";

    public override string? HeaderSubtitle => "Curated groupings of your catalog";

    public ObservableCollection<CollectionViewModel> Collections { get; }

    public ObservableCollection<GameViewModel> VisibleGames { get; } = new();

    public int ShownCount => VisibleGames.Count;

    public bool HasCollections => Collections.Count > 0;

    public bool HasGames => VisibleGames.Count > 0;

    public string CountLabel => SelectedCollection is { } collection
        ? $"{VisibleGames.Count} of {collection.Games.Count} games in {collection.Name}"
        : (HasCollections ? "Pick a collection" : "No collections yet");

    [ObservableProperty]
    private CollectionViewModel? _selectedCollection;

    [ObservableProperty]
    private string? _searchText;

    public string? SearchQuery
    {
        get => SearchText;
        set => SearchText = value;
    }

    public string EditAutomationName => SelectedCollection is { } selected
        ? $"Edit collection {selected.Name}"
        : "Edit collection";

    public void Refresh() => RefillVisibleGames();

    /// <summary>Adds a freshly created collection and selects it.</summary>
    public void AddCollection(GameCollection definition)
    {
        if (Collections.Any(c => c.Id == definition.Id))
        {
            return;
        }
        var gamesById = _allGames.ToDictionary(g => g.Id, g => g);
        var viewModel = new CollectionViewModel(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.GameIds.Where(gamesById.ContainsKey).Select(id => gamesById[id]).ToList(),
            SelectCollection);
        Collections.Add(viewModel);
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(CountLabel));
        SelectedCollection = viewModel;
    }

    /// <summary>Applies a saved edit (rename/re-member) to the matching collection.</summary>
    public void ApplyCollectionUpdate(GameCollection definition)
    {
        var viewModel = Collections.FirstOrDefault(c => c.Id == definition.Id);
        if (viewModel is null)
        {
            return;
        }
        var gamesById = _allGames.ToDictionary(g => g.Id, g => g);
        viewModel.ApplyUpdate(
            definition.Name,
            definition.Description,
            definition.GameIds.Where(gamesById.ContainsKey).Select(id => gamesById[id]).ToList());
        RefillVisibleGames();
    }

    /// <summary>Removes a deleted collection, selecting another one when available.</summary>
    public void RemoveCollection(CollectionViewModel collection)
    {
        var index = Collections.IndexOf(collection);
        if (index < 0)
        {
            return;
        }
        Collections.Remove(collection);
        OnPropertyChanged(nameof(HasCollections));
        if (ReferenceEquals(SelectedCollection, collection))
        {
            SelectedCollection = Collections.Count > 0
                ? Collections[Math.Min(index, Collections.Count - 1)]
                : null;
        }
        OnPropertyChanged(nameof(CountLabel));
    }

    /// <summary>Includes newly imported games in the member editor's source list.</summary>
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
    }

    /// <summary>Toggles one game's membership in one collection (details-page chips).</summary>
    public void SetGameMembership(string gameId, string collectionId, bool member)
    {
        var collection = Collections.FirstOrDefault(c => c.Id == collectionId);
        var game = _allGames.FirstOrDefault(g => g.Id == gameId);
        if (collection is null || game is null)
        {
            return;
        }
        var has = collection.Games.Any(g => g.Id == gameId);
        if (member && !has)
        {
            collection.AddGame(game);
        }
        else if (!member && has)
        {
            collection.RemoveGame(game);
        }
        if (ReferenceEquals(collection, SelectedCollection))
        {
            RefillVisibleGames();
        }
    }

    /// <summary>Drops a removed game from every collection's member list.</summary>
    public void RemoveGame(GameViewModel game)
    {
        game.PropertyChanged -= OnGamePropertyChanged;
        _allGames.Remove(game);
        foreach (var collection in Collections)
        {
            collection.RemoveGame(game);
        }
        RefillVisibleGames();
    }

    partial void OnSelectedCollectionChanged(CollectionViewModel? value)
    {
        foreach (var collection in Collections)
        {
            collection.IsSelected = ReferenceEquals(collection, value);
        }
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(EditAutomationName));
        RefillVisibleGames();
    }

    partial void OnSearchTextChanged(string? value) => RefillVisibleGames();

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Card content (title, play info) updates via shared bindings; nothing to recompute.
    }

    private void SelectCollection(CollectionViewModel collection) => SelectedCollection = collection;

    private void RefillVisibleGames()
    {
        var query = SearchText?.Trim();
        var games = (SelectedCollection?.Games ?? Array.Empty<GameViewModel>())
            .Where(g => string.IsNullOrEmpty(query) || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        VisibleGames.Clear();
        foreach (var game in games)
        {
            VisibleGames.Add(game);
        }
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(HasGames));
        // Raise after the refill so the label reads the fresh VisibleGames count.
        OnPropertyChanged(nameof(CountLabel));
    }

    [RelayCommand]
    private Task CreateCollection() => _requestCreate();

    [RelayCommand]
    private Task EditSelected() =>
        SelectedCollection is { } collection ? _requestEdit(collection) : Task.CompletedTask;
}
