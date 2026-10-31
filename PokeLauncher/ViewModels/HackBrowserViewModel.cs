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

public enum HackSort
{
    Name,
    Status,
    Difficulty,
    BaseGame,
}

public sealed class HackSortOption
{
    public HackSortOption(string label, HackSort mode)
    {
        Label = label;
        Mode = mode;
    }

    public string Label { get; }

    public HackSort Mode { get; }
}

/// <summary>
/// The ROM Hack Browser: the user's own hack library. Hacks are added, edited, and
/// removed here; each one is playable through its base game's emulator once a patched
/// ROM file is attached. Starts empty — PokeLauncher does not ship or download hacks.
/// </summary>
public sealed partial class HackBrowserViewModel : PageViewModelBase, ISearchablePage, IRefreshablePage
{
    private readonly ObservableCollection<RomHackViewModel> _allHacks;
    private readonly Func<Task> _requestCreate;
    private readonly Action<RomHackViewModel> _requestEdit;
    private readonly Action<RomHackViewModel> _openHack;

    public HackBrowserViewModel(
        ObservableCollection<RomHackViewModel> hacks,
        IReadOnlyList<string> baseGameTitles,
        Func<Task> requestCreate,
        Action<RomHackViewModel> requestEdit,
        Action<RomHackViewModel> openHack)
    {
        _allHacks = hacks;
        _requestCreate = requestCreate;
        _requestEdit = requestEdit;
        _openHack = openHack;
        foreach (var hack in _allHacks)
        {
            hack.PropertyChanged += OnHackPropertyChanged;
        }
        _selectedGeneration = FilterOptions.Generations[0];
        _selectedBaseGame = "All base games";
        _selectedGenre = "All genres";
        _selectedDifficulty = "All difficulties";
        _selectedStatus = "All statuses";
        _selectedSort = SortOptions[0];
        BaseGameOptions = new[] { "All base games" }
            .Concat(baseGameTitles)
            .ToList();
        Recalculate();
    }

    public override string DisplayName => "ROM Hack Browser";

    public override string IconGlyph => "\uE774";

    public override string? HeaderSubtitle => "Your own fan-made projects — add a hack, attach its patched ROM, and launch it";

    public ObservableCollection<RomHackViewModel> FilteredHacks { get; } = new();

    public int TotalCount => _allHacks.Count;

    public int ShownCount => FilteredHacks.Count;

    public bool HasResults => FilteredHacks.Count > 0;

    public bool HasHacks => _allHacks.Count > 0;

    public string CountLabel => $"{ShownCount} of {TotalCount} ROM hacks";

    public string CountWords => $"{TotalCount} ROM hack{(TotalCount == 1 ? "" : "s")} in your library";

    public IReadOnlyList<string> BaseGameOptions { get; }

    public IReadOnlyList<string> GenreOptions { get; } = new[]
    {
        "All genres", RomHack.DefaultGenre, "Story & Difficulty", "Exploration", "Story", "Challenge",
        "Quality of Life", "Remake Style", "Randomizer",
    };

    public IReadOnlyList<HackSortOption> SortOptions { get; } = new HackSortOption[]
    {
        new("Name (A–Z)", HackSort.Name),
        new("Status", HackSort.Status),
        new("Difficulty", HackSort.Difficulty),
        new("Base game", HackSort.BaseGame),
    };

    public IReadOnlyList<GenOption> GenerationOptions => FilterOptions.Generations;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private GenOption? _selectedGeneration;

    [ObservableProperty]
    private string? _selectedBaseGame;

    [ObservableProperty]
    private string? _selectedGenre;

    [ObservableProperty]
    private string? _selectedDifficulty;

    [ObservableProperty]
    private string? _selectedStatus;

    [ObservableProperty]
    private HackSortOption? _selectedSort;

    public string? SearchQuery
    {
        get => SearchText;
        set => SearchText = value;
    }

    public void Refresh() => Recalculate();

    /// <summary>Adds a freshly created hack and opens it in the filters.</summary>
    public void AddHack(RomHackViewModel hack)
    {
        if (_allHacks.Any(existing => existing.Id == hack.Id))
        {
            return;
        }
        hack.PropertyChanged += OnHackPropertyChanged;
        _allHacks.Add(hack);
        OnPropertyChanged(nameof(HasHacks));
        OnPropertyChanged(nameof(CountWords));
        Recalculate();
    }

    /// <summary>Applies a saved edit by swapping in the updated view model.</summary>
    public void ApplyHackUpdate(RomHackViewModel hack)
    {
        var existing = _allHacks.FirstOrDefault(h => h.Id == hack.Id);
        if (existing is null)
        {
            AddHack(hack);
            return;
        }
        existing.ApplyUpdate(hack.Snapshot());
        Recalculate();
    }

    /// <summary>Drops a deleted hack.</summary>
    public void RemoveHack(RomHackViewModel hack)
    {
        hack.PropertyChanged -= OnHackPropertyChanged;
        _allHacks.Remove(hack);
        OnPropertyChanged(nameof(HasHacks));
        OnPropertyChanged(nameof(CountWords));
        Recalculate();
    }

    partial void OnSearchTextChanged(string? value) => Recalculate();

    partial void OnSelectedGenerationChanged(GenOption? value) => Recalculate();

    partial void OnSelectedBaseGameChanged(string? value) => Recalculate();

    partial void OnSelectedGenreChanged(string? value) => Recalculate();

    partial void OnSelectedDifficultyChanged(string? value) => Recalculate();

    partial void OnSelectedStatusChanged(string? value) => Recalculate();

    partial void OnSelectedSortChanged(HackSortOption? value) => Recalculate();

    private void OnHackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RomHackViewModel.HasHealthyRom) or nameof(RomHackViewModel.IsRunning))
        {
            OnPropertyChanged(nameof(CountLabel));
        }
    }

    private void Recalculate()
    {
        var query = SearchText?.Trim();
        IEnumerable<RomHackViewModel> hacks = _allHacks;

        if (!string.IsNullOrEmpty(query))
        {
            hacks = hacks.Where(h =>
                h.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                h.Creator.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                h.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (SelectedGeneration?.Value is { } generation)
        {
            hacks = hacks.Where(h => h.Generation == generation);
        }
        if (!string.IsNullOrEmpty(SelectedBaseGame) && SelectedBaseGame != "All base games")
        {
            hacks = hacks.Where(h => h.BaseGameTitle == SelectedBaseGame);
        }
        if (!string.IsNullOrEmpty(SelectedGenre) && SelectedGenre != "All genres")
        {
            hacks = hacks.Where(h => h.Genre == SelectedGenre);
        }
        if (!string.IsNullOrEmpty(SelectedDifficulty) && SelectedDifficulty != "All difficulties")
        {
            hacks = hacks.Where(h => h.Difficulty == SelectedDifficulty);
        }
        if (!string.IsNullOrEmpty(SelectedStatus) && SelectedStatus != "All statuses")
        {
            hacks = hacks.Where(h => h.Status == SelectedStatus);
        }

        hacks = SelectedSort?.Mode switch
        {
            HackSort.Status => hacks.OrderBy(h => h.Status, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase),
            HackSort.Difficulty => hacks.OrderBy(h => FilterOptions.DifficultyRank(h.Difficulty)).ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase),
            HackSort.BaseGame => hacks.OrderBy(h => h.BaseGameTitle, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase),
            _ => hacks.OrderBy(h => h.Title, StringComparer.OrdinalIgnoreCase),
        };

        FilteredHacks.Clear();
        foreach (var hack in hacks)
        {
            FilteredHacks.Add(hack);
        }
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CountLabel));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedGeneration = FilterOptions.Generations[0];
        SelectedBaseGame = "All base games";
        SelectedGenre = "All genres";
        SelectedDifficulty = "All difficulties";
        SelectedStatus = "All statuses";
        SelectedSort = SortOptions[0];
    }

    [RelayCommand]
    private Task CreateHack() => _requestCreate();

    [RelayCommand]
    private void OpenHackDetails(RomHackViewModel? hack)
    {
        if (hack is not null)
        {
            _openHack(hack);
        }
    }
}
