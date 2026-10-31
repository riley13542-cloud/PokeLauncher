using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>
/// UI wrapper around a <see cref="PokemonGame"/> library entry. One instance per game is shared
/// across all pages so favorite toggles, ROM imports, metadata edits, and playtime updates stay
/// consistent everywhere. The view model owns the live state (it changes when the user imports,
/// edits, or plays); <see cref="Snapshot"/> converts it back to the persistence model.
/// </summary>
public partial class GameViewModel : ViewModelBase
{
    private readonly Action<GameViewModel> _open;

    public GameViewModel(PokemonGame model, Action<GameViewModel> open)
    {
        _open = open;
        Id = model.Id;
        ImportedAt = model.ImportedAt;
        _title = model.Title;
        _generation = model.Generation;
        _platform = model.Platform;
        _releaseYear = model.ReleaseYear;
        _description = model.Description;
        _isFavorite = model.IsFavorite;
        _romPath = model.RomPath;
        _lastPlayed = model.LastPlayed;
        _playtimeHours = model.PlaytimeHours;
        _isImported = model.IsImported;
        CoverFrom = Color.Parse(model.CoverFromHex);
        CoverTo = Color.Parse(model.CoverToHex);
        CoverBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(CoverFrom, 0),
                new GradientStop(CoverTo, 1),
            },
        };
    }

    public string Id { get; }

    /// <summary>When the user imported this game's ROM (null for catalog games).</summary>
    public DateTime? ImportedAt { get; }

    /// <summary>"Added 30 Oct 2026" line for the details page; empty for catalog games.</summary>
    public string AddedText => ImportedAt is { } imported
        ? string.Create(CultureInfo.InvariantCulture, $"Added {imported:d MMM yyyy}")
        : string.Empty;

    /// <summary>Whether a custom cover image should be shown for this game.</summary>
    public bool HasCoverArt => CoverArtMode.ShowCustomCovers && CoverImage is not null;

    /// <summary>
    /// The game's custom cover image, loaded once from the managed Covers folder
    /// (null when the game has no cover or custom covers are switched off).
    /// </summary>
    public IImage? CoverImage
    {
        get
        {
            if (!_coverChecked)
            {
                _coverChecked = true;
                var path = ManagedLibrary.FindCover(Id) ?? FindBundledCover(Id);
                _coverImage = null;
                if (path is not null)
                {
                    _coverImage = LoadCoverFrom(path);
                }
            }
            return CoverArtMode.ShowCustomCovers ? _coverImage : null;
        }
    }
    private IImage? _coverImage;
    private bool _coverChecked;

    /// <summary>Bundled cover shipped with the app (Assets\Covers\<game-id>.png/.jpg/…).</summary>
    private static string? FindBundledCover(string gameId)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
        {
            var uri = new Uri($"avares://PokeLauncher/Assets/Covers/{gameId}{extension}");
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                return $"avares:{uri.AbsolutePath}";
            }
            catch
            {
                /* not bundled for this game */
            }
        }
        return null;
    }

    private static IImage? LoadCoverFrom(string path)
    {
        try
        {
            if (path.StartsWith("avares:", StringComparison.Ordinal))
            {
                using var assetStream = Avalonia.Platform.AssetLoader.Open(new Uri(path));
                return new Bitmap(assetStream);
            }
            using var fileStream = File.OpenRead(path);
            return new Bitmap(fileStream);
        }
        catch
        {
            return null;
        }
    }
    /// <summary>Re-checks the disk for a cover (after set/remove or a mode change).</summary>
    public void RefreshCover()
    {
        _coverChecked = false;
        OnPropertyChanged(nameof(CoverImage));
        OnPropertyChanged(nameof(HasCoverArt));
    }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private int _generation;

    [ObservableProperty]
    private string _platform;

    [ObservableProperty]
    private int _releaseYear;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string? _romPath;

    [ObservableProperty]
    private DateTime? _lastPlayed;

    [ObservableProperty]
    private double _playtimeHours;

    [ObservableProperty]
    private bool _isImported;

    /// <summary>Set by the launcher while the game's emulator process is alive.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonLabel))]
    private bool _isRunning;

    public Color CoverFrom { get; }

    public Color CoverTo { get; }

    public IBrush CoverBrush { get; }

    // ------------------------------------------------------------ derived properties

    public string GenerationLabel => $"Generation {FilterOptions.RomanLabel(Generation)}";

    public string GenerationShort => $"Gen {FilterOptions.RomanLabel(Generation)}";

    public string MetaLine => $"{GenerationShort} · {Platform}";

    /// <summary>Year chip text; imported games may not have a known year.</summary>
    public string ReleaseYearText => ReleaseYear > 0 ? $"Released {ReleaseYear}" : "Year unknown";

    public bool HasRom => !string.IsNullOrEmpty(RomPath);

    /// <summary>True when a ROM is recorded but the file has vanished from disk.</summary>
    public bool IsRomMissing => HasRom && !File.Exists(RomPath);

    public bool HasHealthyRom => HasRom && !IsRomMissing;

    public string RomFileStatus => IsRomMissing
        ? "ROM file missing"
        : HasRom ? Path.GetFileName(RomPath)! : "Not imported";

    public bool IsPlayed => LastPlayed is not null;

    public string LastPlayedText => LastPlayed is { } last ? Humanize(last) : "Never played";

    public string PlaytimeText => PlaytimeLabel ?? "—";

    /// <summary>
    /// Human playtime: hours when long enough, minutes/seconds for fresh sessions so a
    /// first short play shows up instead of rounding away to "0 h".
    /// </summary>
    public string? PlaytimeLabel => PlaytimeHours switch
    {
        <= 0 => null,
        _ => FormatHours(PlaytimeHours),
    };

    /// <summary>Shared duration formatting for playtime totals and per-session lines.</summary>
    public static string FormatHours(double hours) => hours switch
    {
        <= 0 => "0 sec",
        < 1.0 / 60.0 => $"{Math.Round(hours * 3600)} sec",
        < 0.25 => $"{Math.Round(hours * 60)} min",
        _ => $"{hours.ToString("0.#", CultureInfo.InvariantCulture)} h",
    };

    /// <summary>Compact "playtime · last played" line for cards; null when the game was never played.</summary>
    public string? PlayInfoText => LastPlayed is { } last
        ? $"{PlaytimeLabel ?? "untracked"} · {Humanize(last)}"
        : PlaytimeLabel;

    /// <summary>Play button label reflects a running emulator so double-launches are obvious.</summary>
    public string PlayButtonLabel => IsRunning ? "Playing…" : "Play";

    public string RomEditorAutomationName => $"Import ROM for {Title}";

    public string EditAutomationName => $"Edit details for {Title}";

    // ------------------------------------------------------------ state updates

    /// <summary>Current state as a persistence-model snapshot (launcher + database writes).</summary>
    public PokemonGame Snapshot() => new()
    {
        Id = Id,
        Title = Title,
        Generation = Generation,
        Platform = Platform,
        ReleaseYear = ReleaseYear,
        Description = Description,
        CoverFromHex = CoverFrom.ToString(),
        CoverToHex = CoverTo.ToString(),
        IsFavorite = IsFavorite,
        RomPath = RomPath,
        LastPlayed = LastPlayed,
        PlaytimeHours = PlaytimeHours,
        IsImported = IsImported,
        ImportedAt = ImportedAt,
    };

    /// <summary>Called after a ROM import/attach so every page reflects the new file.</summary>
    public void ApplyRomPath(string? romPath) => RomPath = romPath;

    /// <summary>Called when a play session ends (and at launch) so cards update live.</summary>
    public void ApplyPlayState(DateTime? lastPlayed, double playtimeHours)
    {
        LastPlayed = lastPlayed;
        PlaytimeHours = playtimeHours;
    }

    /// <summary>Called after the user edits metadata in the edit dialog.</summary>
    public void ApplyMetadata(string title, string platform, int generation, string description)
    {
        Title = title;
        Platform = platform;
        Generation = generation;
        Description = description;
    }

    partial void OnTitleChanged(string value) => RefreshDerived();

    partial void OnGenerationChanged(int value) => RefreshDerived();

    partial void OnPlatformChanged(string value) => RefreshDerived();

    partial void OnReleaseYearChanged(int value) => OnPropertyChanged(nameof(ReleaseYearText));

    partial void OnRomPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasRom));
        OnPropertyChanged(nameof(IsRomMissing));
        OnPropertyChanged(nameof(HasHealthyRom));
        OnPropertyChanged(nameof(RomFileStatus));
        OnPropertyChanged(nameof(ReleaseYearText));
    }

    partial void OnLastPlayedChanged(DateTime? value) => RefreshDerived();

    partial void OnPlaytimeHoursChanged(double value) => RefreshDerived();

    partial void OnIsImportedChanged(bool value) => RefreshDerived();

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(GenerationLabel));
        OnPropertyChanged(nameof(GenerationShort));
        OnPropertyChanged(nameof(MetaLine));
        OnPropertyChanged(nameof(IsPlayed));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(PlaytimeText));
        OnPropertyChanged(nameof(PlaytimeLabel));
        OnPropertyChanged(nameof(PlayInfoText));
        OnPropertyChanged(nameof(IsRomMissing));
        OnPropertyChanged(nameof(HasHealthyRom));
        OnPropertyChanged(nameof(RomFileStatus));
        OnPropertyChanged(nameof(RomEditorAutomationName));
        OnPropertyChanged(nameof(EditAutomationName));
    }

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private void ToggleFavorite() => IsFavorite = !IsFavorite;

    [RelayCommand]
    private void Open() => _open(this);

    private static string Humanize(DateTime when)
    {
        var days = (int)(DateTime.Now.Date - when.Date).TotalDays;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 7 => $"{days} days ago",
            < 30 => $"{days / 7} week{(days / 7 == 1 ? "" : "s")} ago",
            _ => when.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }
}
