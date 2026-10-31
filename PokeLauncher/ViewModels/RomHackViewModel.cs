using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>
/// UI wrapper around a user-created <see cref="RomHack"/> entry. Shared across pages so
/// bookmarks, launch state, and edits stay consistent between Home, the browser, and details.
/// </summary>
public partial class RomHackViewModel : ViewModelBase
{
    private readonly Action<RomHackViewModel> _open;

    public RomHackViewModel(RomHack model, Action<RomHackViewModel> open)
    {
        _open = open;
        Id = model.Id;
        Title = model.Title;
        BaseGameId = model.BaseGameId;
        Description = model.Description;
        Creator = model.Creator;
        ExternalUrl = model.ExternalUrl;
        RomPath = model.RomPath;
        Genre = model.Genre;
        Difficulty = model.Difficulty;
        Status = model.Status;
        CreatedAt = model.CreatedAt;
        _isBookmarked = model.IsBookmarked;
        CoverFrom = Color.Parse("#7C3AED");
        CoverTo = Color.Parse("#312E81");
    }

    public string Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _baseGameId;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _creator;

    [ObservableProperty]
    private string? _externalUrl;

    [ObservableProperty]
    private string? _romPath;

    [ObservableProperty]
    private string _genre;

    [ObservableProperty]
    private string _difficulty;

    [ObservableProperty]
    private string _status;

    public DateTime? CreatedAt { get; }

    [ObservableProperty]
    private bool _isBookmarked;

    partial void OnIsBookmarkedChanged(bool value) => OnPropertyChanged(nameof(BookmarkLabel));

    public string BookmarkLabel => IsBookmarked ? "Bookmarked" : "Bookmark";

    /// <summary>Set by the launcher while the hack's emulator process is alive.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public string PlayButtonLabel => IsRunning ? "Playing…" : "Play";

    public Color CoverFrom { get; }

    public Color CoverTo { get; }


    /// <summary>Alias used by chip styling bindings.</summary>

    public bool IsEasy => Difficulty == "Easy";

    public bool IsMedium => Difficulty == "Medium";

    public bool IsHard => Difficulty == "Hard";

    public bool IsVeryHard => Difficulty == "Very Hard";

    public bool IsComplete => Status == "Complete";

    public bool IsBeta => Status == "Beta";

    public bool IsWip => Status == "In Development";

    public bool IsDiscontinued => Status == "Discontinued";

    public bool HasRom => !string.IsNullOrEmpty(RomPath);

    /// <summary>True when a ROM is recorded but the file has vanished from disk.</summary>
    public bool IsRomMissing => HasRom && !System.IO.File.Exists(RomPath);

    public bool HasHealthyRom => HasRom && !IsRomMissing;

    public string RomFileStatus => IsRomMissing
        ? "ROM file missing"
        : HasRom ? System.IO.Path.GetFileName(RomPath)! : "No patched ROM attached";

    public bool HasExternalUrl => !string.IsNullOrWhiteSpace(ExternalUrl);

    /// <summary>Set after MainViewModel resolves the base game (title/platform/generation).</summary>
    [ObservableProperty]
    private string _baseGameTitle = string.Empty;

    [ObservableProperty]
    private string _platform = string.Empty;

    [ObservableProperty]
    private int _generation;

    public string GenerationLabel => Generation > 0
        ? $"Generation {FilterOptions.RomanLabel(Generation)}"
        : string.Empty;

    public string MetaLine => string.IsNullOrEmpty(Platform)
        ? BaseGameTitle
        : $"{BaseGameTitle} · {Platform}";

    [RelayCommand]
    private void ToggleBookmark() => IsBookmarked = !IsBookmarked;

    [RelayCommand]
    private void Open() => _open(this);

    /// <summary>Applies a saved edit (all fields) so pages reflect the database.</summary>
    public void ApplyUpdate(RomHack model)
    {
        Title = model.Title;
        BaseGameId = model.BaseGameId;
        Description = model.Description;
        Creator = model.Creator;
        ExternalUrl = model.ExternalUrl;
        RomPath = model.RomPath;
        Genre = model.Genre;
        Difficulty = model.Difficulty;
        Status = model.Status;
        OnPropertyChanged(nameof(IsEasy));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsHard));
        OnPropertyChanged(nameof(IsVeryHard));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsBeta));
        OnPropertyChanged(nameof(IsWip));
        OnPropertyChanged(nameof(IsDiscontinued));
        OnPropertyChanged(nameof(HasRom));
        OnPropertyChanged(nameof(IsRomMissing));
        OnPropertyChanged(nameof(HasHealthyRom));
        OnPropertyChanged(nameof(RomFileStatus));
    }

    /// <summary>Current state as a persistence-model snapshot.</summary>
    public RomHack Snapshot() => new()
    {
        Id = Id,
        Title = Title,
        BaseGameId = BaseGameId,
        Description = Description,
        Creator = Creator,
        ExternalUrl = string.IsNullOrWhiteSpace(ExternalUrl) ? null : ExternalUrl,
        RomPath = RomPath,
        Genre = Genre,
        Difficulty = Difficulty,
        Status = Status,
        CreatedAt = CreatedAt,
        IsBookmarked = IsBookmarked,
    };
}
