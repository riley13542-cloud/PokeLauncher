using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>Shared choice lists for the import and edit dialogs.</summary>
public static class GameEditOptions
{
    /// <summary>Platforms a game can be assigned, in launch order.</summary>
    public static IReadOnlyList<string> Platforms { get; } = EmulatorSystems.All
        .Select(s => s.DisplayName)
        .ToArray();

    /// <summary>Generations I–IV (no "all" sentinel; games always have a concrete generation).</summary>
    public static IReadOnlyList<GenOption> Generations { get; } = new GenOption[]
    {
        new(1, "Generation I"),
        new(2, "Generation II"),
        new(3, "Generation III"),
        new(4, "Generation IV"),
    };

    public static string GenerationLabel(int generation) =>
        Generations.FirstOrDefault(g => g.Value == generation)?.Label ?? $"Generation {generation}";
}

/// <summary>One reviewed file in the import dialog.</summary>
public sealed partial class ImportRowViewModel : ViewModelBase
{
    public ImportRowViewModel(RomFileAnalysis analysis)
    {
        FilePath = analysis.FilePath;
        FileName = analysis.FileName;
        Kind = analysis.Kind;
        Note = analysis.Note;
        IsArchive = analysis.IsArchive;
        ExistingGame = analysis.ExistingGame;
        _title = analysis.SuggestedTitle;
        _platform = analysis.Platform;
        _selectedGeneration = GameEditOptions.Generations.FirstOrDefault(g => g.Value == analysis.Generation)
                              ?? GameEditOptions.Generations[0];
    }

    public string FilePath { get; }

    public string FileName { get; }

    public RomImportKind Kind { get; }

    public string? Note { get; }

    public bool IsArchive { get; }

    public PokemonGame? ExistingGame { get; }

    /// <summary>Bound directly on the row so the ComboBox has items before its selection binding runs.</summary>
    public IReadOnlyList<string> PlatformChoices => GameEditOptions.Platforms;

    public IReadOnlyList<GenOption> GenerationChoices => GameEditOptions.Generations;

    /// <summary>True when confirming this row performs an action (new entry or attach).</summary>
    public bool IsImportable => Kind is RomImportKind.New or RomImportKind.Attach;

    public bool IsTitleEditable => Kind == RomImportKind.New;

    /// <summary>Chip text summarizing what will happen with this file.</summary>
    public string StatusText => Kind switch
    {
        RomImportKind.New => "New game",
        RomImportKind.Attach => $"Adds ROM to “{ExistingGame?.Title}”",
        RomImportKind.Duplicate => "Already in library",
        RomImportKind.Unsupported => "Can't import",
        _ => string.Empty,
    };

    /// <summary>
    /// Null-tolerant on purpose: while the review dialog initializes, ComboBox bindings can
    /// push a transient null selection back into the row; the analysis default is kept.
    /// </summary>
    public string Platform
    {
        get => _platform;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                OnPropertyChanged(nameof(Platform)); // re-assert the retained value
                return;
            }
            if (!string.Equals(_platform, value, StringComparison.Ordinal))
            {
                _platform = value;
                OnPropertyChanged(nameof(Platform));
            }
        }
    }
    private string _platform;

    public GenOption SelectedGeneration
    {
        get => _selectedGeneration ?? GameEditOptions.Generations[0];
        set
        {
            if (value is null)
            {
                OnPropertyChanged(nameof(SelectedGeneration));
                return;
            }
            if (!ReferenceEquals(_selectedGeneration, value))
            {
                _selectedGeneration = value;
                OnPropertyChanged(nameof(SelectedGeneration));
            }
        }
    }
    private GenOption? _selectedGeneration;

    [ObservableProperty]
    private string _title;

    /// <summary>Generation chosen for this row (0 only if the option list were empty).</summary>
    public int Generation => SelectedGeneration?.Value ?? 0;

    /// <summary>Validated title for a new-game row; falls back to the file name when blank.</summary>
    public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title.Trim();
}

/// <summary>
/// Review step of the import flow: shows what PokeLauncher detected for each picked file,
/// lets the user correct titles/platforms/generations for new entries, and reports (without
/// importing) files that are duplicates or unsupported. Confirm hands the accepted rows
/// back to <see cref="MainViewModel"/>, which writes them to the database.
/// </summary>
public sealed partial class ImportGamesDialogViewModel : ViewModelBase
{
    public ImportGamesDialogViewModel(IEnumerable<RomFileAnalysis> analyses)
    {
        Rows = new ObservableCollection<ImportRowViewModel>(
            analyses.Select(analysis => new ImportRowViewModel(analysis)));
    }

    public ObservableCollection<ImportRowViewModel> Rows { get; }

    public bool HasImportable => Rows.Any(row => row.IsImportable);

    public int ImportableCount => Rows.Count(row => row.IsImportable);

    public int NewCount => Rows.Count(row => row.Kind == RomImportKind.New);

    public int AttachCount => Rows.Count(row => row.Kind == RomImportKind.Attach);

    public int SkippedCount => Rows.Count - ImportableCount;

    public string ConfirmButtonText => HasImportable
        ? $"Import {ImportableCount} game{(ImportableCount == 1 ? "" : "s")}"
        : "Nothing to import";

    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            if (NewCount > 0)
            {
                parts.Add($"{NewCount} new game{(NewCount == 1 ? "" : "s")}");
            }
            if (AttachCount > 0)
            {
                parts.Add($"{AttachCount} file{(AttachCount == 1 ? "" : "s")} linked to existing games");
            }
            if (SkippedCount > 0)
            {
                parts.Add($"{SkippedCount} skipped");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : "Nothing to import";
        }
    }

    /// <summary>Raised with true to confirm, false to cancel; the window closes itself.</summary>
    public event Action<bool>? RequestClose;

    public IReadOnlyList<ImportRowViewModel> ConfirmedRows() =>
        Rows.Where(row => row.IsImportable).ToArray();

    [RelayCommand]
    private void Confirm() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}

/// <summary>
/// One game row in the collection member editor: a checkbox per library game.
/// </summary>
public sealed partial class CollectionMemberRowViewModel : ViewModelBase
{
    public CollectionMemberRowViewModel(GameViewModel game, bool isMember)
    {
        Game = game;
        IsMember = isMember;
    }

    public GameViewModel Game { get; }

    public string Title => Game.Title;

    public string MetaLine => Game.MetaLine;

    /// <summary>Whether this game is currently in the collection being edited.</summary>
    [ObservableProperty]
    public partial bool IsMember { get; set; }
}

/// <summary>
/// Create/edit dialog state for a collection: name and description, plus the member
/// editor (one checkbox per library game). Nothing is written until the caller applies
/// the result. Deletion (edit mode only) uses the same two-step confirm as game removal.
/// </summary>
public sealed partial class EditCollectionDialogViewModel : ViewModelBase
{
    public EditCollectionDialogViewModel(
        CollectionViewModel? collection,
        IReadOnlyList<GameViewModel> allGames)
    {
        IsCreate = collection is null;
        CollectionId = collection?.Id ?? string.Empty;
        OriginalName = collection?.Name ?? string.Empty;
        _name = collection?.Name ?? string.Empty;
        _description = collection?.Description ?? string.Empty;

        var memberIds = collection?.Games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal)
                        ?? new HashSet<string>(StringComparer.Ordinal);
        Members = new ObservableCollection<CollectionMemberRowViewModel>(
            allGames.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CollectionMemberRowViewModel(g, memberIds.Contains(g.Id))));
    }

    public bool IsCreate { get; }

    public string CollectionId { get; }

    public string OriginalName { get; }

    public string DialogTitle => IsCreate ? "New collection" : "Edit collection";

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    public ObservableCollection<CollectionMemberRowViewModel> Members { get; }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    [ObservableProperty]
    private string? _memberFilter;

    /// <summary>Two-step deletion (edit mode only): first click arms, second click deletes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonLabel))]
    private bool _isDeleteArmed;

    public string DeleteButtonLabel => IsDeleteArmed ? "Confirm delete" : "Delete collection…";

    public bool HasChanges => !string.Equals(Name?.Trim(), OriginalName, StringComparison.Ordinal);

    public event Action<bool>? RequestClose;

    public event Action? DeleteConfirmed;

    [RelayCommand]
    private void Save() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

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

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnMemberFilterChanged(string? value) => OnPropertyChanged(nameof(VisibleMembers));

    /// <summary>Member rows surviving the current filter (the editor list binds to this).</summary>
    public IEnumerable<CollectionMemberRowViewModel> VisibleMembers =>
        string.IsNullOrWhiteSpace(MemberFilter)
            ? Members
            : Members.Where(m => m.Title.Contains(MemberFilter.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Edit dialog state for one game: title, platform, generation, description — plus, for
/// games the user imported, removal from the library (two-step confirm), and for games
/// with a ROM, detaching the file. Nothing is written until the caller applies the result.
/// </summary>
public sealed partial class EditGameDialogViewModel : ViewModelBase
{
    public EditGameDialogViewModel(GameViewModel game)
    {
        GameId = game.Id;
        OriginalTitle = game.Title;
        _title = game.Title;
        _description = game.Description;
        _originalDescription = game.Description;
        _platform = GameEditOptions.Platforms.Contains(game.Platform)
            ? game.Platform
            : GameEditOptions.Platforms[0];
        _selectedGeneration = GameEditOptions.Generations.FirstOrDefault(g => g.Value == game.Generation)
                              ?? GameEditOptions.Generations[0];
        IsImported = game.IsImported;
        HasRom = game.HasRom;
        RomPath = game.RomPath;
        RomFileStatus = game.RomFileStatus;
    }

    public string GameId { get; }

    public string OriginalTitle { get; }

    public IReadOnlyList<string> PlatformChoices => GameEditOptions.Platforms;

    public IReadOnlyList<GenOption> GenerationChoices => GameEditOptions.Generations;

    [ObservableProperty]
    private string _title;

    /// <summary>Null-tolerant like the import rows: combo initialization may push a transient null.</summary>
    public string Platform
    {
        get => _platform;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                OnPropertyChanged(nameof(Platform));
                return;
            }
            if (!string.Equals(_platform, value, StringComparison.Ordinal))
            {
                _platform = value;
                OnPropertyChanged(nameof(Platform));
            }
        }
    }
    private string _platform;

    public GenOption SelectedGeneration
    {
        get => _selectedGeneration ?? GameEditOptions.Generations[0];
        set
        {
            if (value is null)
            {
                OnPropertyChanged(nameof(SelectedGeneration));
                return;
            }
            if (!ReferenceEquals(_selectedGeneration, value))
            {
                _selectedGeneration = value;
                OnPropertyChanged(nameof(SelectedGeneration));
            }
        }
    }
    private GenOption? _selectedGeneration;

    [ObservableProperty]
    private string _description;

    public bool IsImported { get; }

    public bool HasRom { get; }

    public string? RomPath { get; }

    public string RomFileStatus { get; }

    /// <summary>Generation chosen in the dialog; kept in sync for the caller.</summary>
    public int Generation => SelectedGeneration?.Value ?? 0;

    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    /// <summary>Two-step removal: first click arms the confirm state, second click removes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveButtonLabel))]
    private bool _isRemoveArmed;

    public string RemoveButtonLabel => IsRemoveArmed ? "Confirm removal" : "Remove from library…";

    public bool HasChanges =>
        !string.Equals(Title.Trim(), OriginalTitle, StringComparison.Ordinal) ||
        Description != _originalDescription;

    private readonly string _originalDescription;

    public event Action<bool>? RequestClose;

    public event Action? RemoveConfirmed;

    public event Action? DetachRomConfirmed;

    [RelayCommand]
    private void Save() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private void ToggleRemove()
    {
        if (!IsImported)
        {
            return;
        }
        if (IsRemoveArmed)
        {
            RemoveConfirmed?.Invoke();
        }
        else
        {
            IsRemoveArmed = true;
        }
    }

    [RelayCommand]
    private void DetachRom()
    {
        if (HasRom)
        {
            DetachRomConfirmed?.Invoke();
        }
    }

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(CanSave));
}
