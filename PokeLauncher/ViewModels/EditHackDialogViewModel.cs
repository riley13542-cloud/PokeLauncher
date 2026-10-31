using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;

namespace PokeLauncher.ViewModels;

/// <summary>
/// Create/edit dialog state for a user-created ROM hack: title, base game (from the
/// built-in catalog), description, creator, official page URL, and the patched ROM file.
/// Nothing is written until the caller applies the result.
/// </summary>
public sealed partial class EditHackDialogViewModel : ViewModelBase
{
    public EditHackDialogViewModel(
        RomHackViewModel? hack,
        IReadOnlyList<GameViewModel> catalogGames)
    {
        CatalogGames = catalogGames;
        IsCreate = hack is null;
        HackId = hack?.Id ?? string.Empty;
        OriginalTitle = hack?.Title ?? string.Empty;
        _title = hack?.Title ?? string.Empty;
        _description = hack?.Description ?? string.Empty;
        _creator = hack?.Creator ?? string.Empty;
        _url = hack?.ExternalUrl ?? string.Empty;
        _selectedBaseGame = catalogGames.FirstOrDefault(g => g.Id == hack?.BaseGameId)
                            ?? catalogGames.FirstOrDefault()
                            ?? throw new ArgumentException("The catalog is empty.", nameof(catalogGames));
        _selectedGenre = EditHackDialogViewModel.GenreOptions.FirstOrDefault(g => g == hack?.Genre)
                         ?? RomHack.DefaultGenre;
        _selectedDifficulty = DifficultyOptions.FirstOrDefault(d => d == hack?.Difficulty)
                              ?? RomHack.DefaultDifficulty;
        _selectedStatus = StatusOptions.FirstOrDefault(s => s == hack?.Status)
                          ?? RomHack.DefaultStatus;
        _romPath = hack?.RomPath ?? string.Empty;
    }

    public bool IsCreate { get; }

    public string HackId { get; }

    public string OriginalTitle { get; }

    public IReadOnlyList<GameViewModel> CatalogGames { get; }

    public string DialogTitle => IsCreate ? "Add ROM hack" : "Edit ROM hack";



    public static IReadOnlyList<string> GenreOptions { get; } = new[]
    {
        RomHack.DefaultGenre, "Story & Difficulty", "Exploration", "Story", "Challenge",
        "Quality of Life", "Remake Style", "Randomizer",
    };

    public static IReadOnlyList<string> DifficultyOptions { get; } = new[]
    {
        "Easy", "Medium", "Hard", "Very Hard",
    };

    public static IReadOnlyList<string> StatusOptions { get; } = new[]
    {
        "In Development", "Beta", "Complete", "Discontinued",
    };

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _creator;

    [ObservableProperty]
    private string _url;

    [ObservableProperty]
    private GameViewModel _selectedBaseGame;

    [ObservableProperty]
    private string _selectedGenre;

    [ObservableProperty]
    private string _selectedDifficulty;

    [ObservableProperty]
    private string _selectedStatus;

    /// <summary>The attached patched ROM file (set through the Browse button).</summary>
    [ObservableProperty]
    private string _romPath;

    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    public bool HasRom => !string.IsNullOrWhiteSpace(RomPath);

    public string RomFileStatus => HasRom ? System.IO.Path.GetFileName(RomPath) : "No patched ROM attached yet";

    /// <summary>Two-step deletion (edit mode only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonLabel))]
    private bool _isDeleteArmed;

    public string DeleteButtonLabel => IsDeleteArmed ? "Confirm delete" : "Delete hack…";

    public event Action<bool>? RequestClose;

    public event Action? DeleteConfirmed;

    [RelayCommand]
    private void Save() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private void BrowseRom() => BrowseRomRequested?.Invoke(this);

    /// <summary>Raised so the host can open a file picker and set RomPath.</summary>
    public event Action<EditHackDialogViewModel>? BrowseRomRequested;

    /// <summary>Called by the host after a file picker returns a path.</summary>
    public void SetRomPath(string path)
    {
        RomPath = path;
        OnPropertyChanged(nameof(RomPath));
        OnPropertyChanged(nameof(HasRom));
        OnPropertyChanged(nameof(RomFileStatus));
    }

    [RelayCommand]
    private void ToggleDelete()
    {
        if (IsCreate)
        {
            return;
        }
        if (IsDeleteArmed)
        {
            DeleteConfirmed?.Invoke();
        }
        else
        {
            IsDeleteArmed = true;
        }
    }

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>Builds the persistence model from the dialog state.</summary>
    public RomHack BuildModel(string id, DateTime? createdAt, string? resolvedRomPath) => new()
    {
        Id = id,
        Title = Title.Trim(),
        BaseGameId = SelectedBaseGame.Id,
        Description = Description?.Trim() ?? string.Empty,
        Creator = Creator?.Trim() ?? string.Empty,
        ExternalUrl = string.IsNullOrWhiteSpace(Url) ? null : Url.Trim(),
        RomPath = resolvedRomPath,
        Genre = SelectedGenre,
        Difficulty = SelectedDifficulty,
        Status = SelectedStatus,
        CreatedAt = createdAt,
    };
}
