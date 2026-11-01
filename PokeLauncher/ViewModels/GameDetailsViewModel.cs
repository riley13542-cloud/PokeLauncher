using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>One screenshot thumbnail in the details-page gallery.</summary>
public sealed class ScreenshotItem : IDisposable
{
    public ScreenshotItem(string path, Bitmap image)
    {
        Path = path;
        Image = image;
        FileName = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }

    public string FileName { get; }

    public Bitmap Image { get; private set; }

    public void Dispose()
    {
        Image?.Dispose();
        Image = null!;
    }
}

/// <summary>One formatted row of the details page's "Recent sessions" list.</summary>
public sealed record SessionLine(string Duration, string When);

/// <summary>One collection chip on the details page; toggling edits membership.</summary>
public sealed record CollectionChip(string Id, string Name, bool IsMember);

/// <summary>One save-backup row on the details page (restore uses a two-step confirm).</summary>
public sealed partial class SaveBackupRowViewModel : ViewModelBase
{
    public SaveBackupRowViewModel(SaveBackup backup, string createdText)
    {
        Backup = backup;
        _createdText = createdText;
    }

    public SaveBackup Backup { get; }

    [ObservableProperty]
    public partial bool IsRestoreArmed { get; set; }

    [ObservableProperty]
    private string _createdText;

    public string RestoreLabel => IsRestoreArmed ? "Confirm restore" : "Restore";

    partial void OnIsRestoreArmedChanged(bool value) => OnPropertyChanged(nameof(RestoreLabel));
}

/// <summary>
/// Details page for one game. Owns the launch entry point, the ROM-import entry point
/// for games without a file, the metadata editor, and the inline launch-error strip
/// (with a shortcut to Settings when the problem is the emulator configuration). Also
/// shows the game's collections membership and its recent play sessions.
/// </summary>
public sealed partial class GameDetailsViewModel : PageViewModelBase
{
    private readonly Action<string> _showToast;
    private readonly Func<GameViewModel, Task> _playGame;
    private readonly Func<GameViewModel, Task> _importRom;
    private readonly Func<GameViewModel, Task> _editGame;
    private readonly Action _openSettings;
    private readonly Func<string, IReadOnlyList<PlaySessionRecord>> _loadSessions;
    private readonly Func<string, long> _countSessions;
    private readonly Func<string, IReadOnlyList<string>> _loadCollectionNames;
    private readonly Func<string, IReadOnlyList<CollectionChip>> _loadCollectionChips;
    private readonly SaveBackupService _saveBackups;
    private readonly Func<GameViewModel, Task> _setCoverArt;
    private readonly Func<GameViewModel, Task> _removeCoverArt;
    private readonly Func<string, string, bool, Task> _toggleCollection;
    private readonly Func<GameViewModel?, IReadOnlyList<string>>? _loadScreenshots;
    private readonly Action<string>? _openFile;

    public GameDetailsViewModel(
        Action<string> showToast,
        Func<GameViewModel, Task> playGame,
        Func<GameViewModel, Task> importRom,
        Func<GameViewModel, Task> editGame,
        Action openSettings,
        Func<string, IReadOnlyList<PlaySessionRecord>> loadSessions,
        Func<string, long> countSessions,
        Func<string, IReadOnlyList<string>> loadCollectionNames,
        Func<string, IReadOnlyList<CollectionChip>> loadCollectionChips,
        SaveBackupService saveBackups,
        Func<GameViewModel, Task> setCoverArt,
        Func<GameViewModel, Task> removeCoverArt,
        Func<string, string, bool, Task> toggleCollection,
        Func<GameViewModel?, IReadOnlyList<string>>? loadScreenshots = null,
        Action<string>? openFile = null)
    {
        _showToast = showToast;
        _playGame = playGame;
        _importRom = importRom;
        _editGame = editGame;
        _openSettings = openSettings;
        _loadSessions = loadSessions;
        _countSessions = countSessions;
        _loadCollectionNames = loadCollectionNames;
        _loadCollectionChips = loadCollectionChips;
        _saveBackups = saveBackups;
        _setCoverArt = setCoverArt;
        _removeCoverArt = removeCoverArt;
        _toggleCollection = toggleCollection;
        _loadScreenshots = loadScreenshots;
        _openFile = openFile;
    }

    public override string DisplayName => "Game Details";

    public override string IconGlyph => "\uE8A9";

    [ObservableProperty]
    public partial GameViewModel? Game { get; private set; }

    public bool HasGame => Game is not null;

    /// <summary>Last launch error, shown inline so the fix is one click away.</summary>
    [ObservableProperty]
    public partial string? LaunchError { get; private set; }

    [ObservableProperty]
    public partial bool LaunchErrorNeedsEmulator { get; private set; }

    public bool HasLaunchError => LaunchError is not null;

    /// <summary>Most recent play sessions for the shown game (activity section).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SessionLine> RecentSessions { get; private set; } =
        Array.Empty<SessionLine>();

    public bool HasSessions => RecentSessions.Count > 0;

    /// <summary>Total sessions ever recorded for the shown game.</summary>
    [ObservableProperty]
    public partial long TotalSessions { get; private set; }

    public string SessionsCaption => TotalSessions switch
    {
        0 => string.Empty,
        1 => "1 session recorded",
        _ => $"{TotalSessions} sessions recorded",
    };

    /// <summary>Names of the collections containing the shown game.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Collections { get; private set; } =
        Array.Empty<string>();

    public bool HasCollections => Collections.Count > 0;

    public string CollectionsText => HasCollections ? string.Join(", ", Collections) : "None yet";

    /// <summary>Save-backup rows for the shown game (newest first).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SaveBackupRowViewModel> SaveBackups { get; private set; } =
        Array.Empty<SaveBackupRowViewModel>();

    public bool HasSaveBackups => SaveBackups.Count > 0;

    public string SavesCaption
    {
        get
        {
            var total = SaveBackups.Sum(row => row.Backup.TotalBytes);
            return SaveBackups.Count switch
            {
                0 => string.Empty,
                1 => $"1 backup · {SaveBackup.FormatSize(total)}",
                _ => $"{SaveBackups.Count} backups · {SaveBackup.FormatSize(total)} total",
            };
        }
    }

    public void SetGame(GameViewModel? game)
    {
        Game = game;
        ClearLaunchError();
        ReloadActivity();
    }

    /// <summary>Re-reads sessions, collection membership, and save backups (after a
    /// session ends or an edit dialog commits, so the page never shows stale activity).</summary>
    public void ReloadActivity()
    {
        if (Game is not { } game)
        {
            RecentSessions = Array.Empty<SessionLine>();
            TotalSessions = 0;
            Collections = Array.Empty<string>();
            CollectionChips = Array.Empty<CollectionChip>();
            SaveBackups = Array.Empty<SaveBackupRowViewModel>();
            ClearScreenshots();
            OnPropertyChanged(nameof(HasSessions));
            OnPropertyChanged(nameof(SessionsCaption));
            OnPropertyChanged(nameof(HasCollections));
            OnPropertyChanged(nameof(CollectionsText));
            OnPropertyChanged(nameof(HasSaveBackups));
            OnPropertyChanged(nameof(SavesCaption));
            return;
        }

        var sessions = _loadSessions(game.Id);
        var lines = new List<SessionLine>();
        foreach (var session in sessions)
        {
            lines.Add(new SessionLine(
                GameViewModel.FormatHours(session.DurationSeconds / 3600.0),
                HumanizeDay(session.StartedAt)));
        }
        RecentSessions = lines;
        TotalSessions = _countSessions(game.Id);
        Collections = _loadCollectionNames(game.Id);
        CollectionChips = _loadCollectionChips(game.Id);
        OnPropertyChanged(nameof(HasCollectionChips));
        RefreshSaves();
        LoadScreenshots(game);
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(SessionsCaption));
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(CollectionsText));
    }

    // ---------------------------------------------------------- screenshots gallery

    public ScreenshotItem[] Screenshots { get; private set; } = Array.Empty<ScreenshotItem>();

    public bool HasScreenshots => Screenshots.Length > 0;

    private void LoadScreenshots(GameViewModel game)
    {
        ClearScreenshots();
        if (_loadScreenshots is null)
        {
            return;
        }
        try
        {
            var items = new List<ScreenshotItem>();
            foreach (var path in _loadScreenshots(game).Take(12))
            {
                using var stream = File.OpenRead(path);
                items.Add(new ScreenshotItem(path, Bitmap.DecodeToWidth(stream, 320)));
            }
            Screenshots = items.ToArray();
            OnPropertyChanged(nameof(HasScreenshots));
        }
        catch
        {
            /* a decode failure leaves the gallery empty instead of breaking the page */
        }
    }

    private void ClearScreenshots()
    {
        foreach (var item in Screenshots)
        {
            item.Dispose();
        }
        Screenshots = Array.Empty<ScreenshotItem>();
        OnPropertyChanged(nameof(HasScreenshots));
    }

    [RelayCommand]
    private void OpenScreenshot(ScreenshotItem? item)
    {
        if (item is not null && _openFile is not null)
        {
            _openFile(item.Path);
        }
    }

    /// <summary>Re-reads the game's save-backup list from disk (keeping a pending
    /// restore-confirmation armed when the same backup still exists).</summary>
    private void RefreshSaves()
    {
        var armed = SaveBackups.FirstOrDefault(row => row.IsRestoreArmed);
        SaveBackups = _saveBackups
            .ListBackups(Game?.Id ?? string.Empty)
            .Select(backup => new SaveBackupRowViewModel(
                backup, $"{HumanizeDay(backup.CreatedAt)} · {backup.CreatedAt:HH:mm}"))
            .ToList();
        if (armed is not null)
        {
            var stillThere = SaveBackups.FirstOrDefault(row => row.Backup.Name == armed.Backup.Name);
            if (stillThere is not null)
            {
                stillThere.IsRestoreArmed = true;
            }
        }
        OnPropertyChanged(nameof(HasSaveBackups));
        OnPropertyChanged(nameof(SavesCaption));
    }

    public void ShowLaunchError(string? message, bool needsEmulator)
    {
        LaunchError = message;
        LaunchErrorNeedsEmulator = needsEmulator;
    }

    public void ClearLaunchError()
    {
        LaunchError = null;
        LaunchErrorNeedsEmulator = false;
    }

    partial void OnGameChanged(GameViewModel? value) => OnPropertyChanged(nameof(HasGame));

    partial void OnLaunchErrorChanged(string? value) => OnPropertyChanged(nameof(HasLaunchError));

    [RelayCommand]
    private Task Play() => Game is { } game ? _playGame(game) : Task.CompletedTask;

    [RelayCommand]
    private Task ImportRom() => Game is { } game ? _importRom(game) : Task.CompletedTask;

    [RelayCommand]
    private Task EditDetails() => Game is { } game ? _editGame(game) : Task.CompletedTask;

    [RelayCommand]
    private void OpenEmulatorSettings() => _openSettings();

    [RelayCommand]
    private void DismissLaunchError() => ClearLaunchError();

    // ==================================================================== saves

    /// <summary>Manual "Back up now": snapshots the current save files on demand.</summary>
    [RelayCommand]
    private async Task BackUpNowAsync()
    {
        if (Game is not { } game)
        {
            return;
        }
        if (game.IsRunning)
        {
            _showToast("The emulator is running — close it before taking a save backup.");
            return;
        }
        if (!game.HasHealthyRom)
        {
            _showToast(game.HasRom
                ? "The ROM file is missing, so there are no saves to back up."
                : "Import a ROM file first — saves appear next to it when you play.");
            return;
        }
        try
        {
            var backup = await Task.Run(() => _saveBackups.CreateBackup(game.Id, game.RomPath ?? string.Empty));
            RefreshSaves();
            _showToast(backup is null
                ? "No save files found yet — backups appear after your first play."
                : $"Save backup created ({backup.Summary}).");
        }
        catch (Exception exception)
        {
            _showToast($"The save backup failed: {exception.Message}");
        }
    }

    /// <summary>Restore with a two-step confirm: the first click arms the row, the second
    /// takes a safety snapshot of the current saves and then restores the chosen one.</summary>
    [RelayCommand]
    private async Task RestoreBackupAsync(SaveBackupRowViewModel? row)
    {
        if (Game is not { } game || row is null)
        {
            return;
        }
        if (game.IsRunning)
        {
            _showToast("The emulator is running — close it before restoring saves.");
            return;
        }
        if (!game.HasHealthyRom)
        {
            _showToast("The ROM file is missing, so there is no save folder to restore into.");
            return;
        }
        if (!row.IsRestoreArmed)
        {
            foreach (var other in SaveBackups)
            {
                other.IsRestoreArmed = false;
            }
            row.IsRestoreArmed = true;
            return;
        }

        row.IsRestoreArmed = false;
        try
        {
            var safety = await Task.Run(() => _saveBackups.Restore(game.Id, row.Backup, game.RomPath ?? string.Empty));
            RefreshSaves();
            _showToast(safety is null
                ? $"Save restored from {row.Backup.CreatedAt:HH:mm}."
                : $"Save restored from {row.Backup.CreatedAt:HH:mm}. The previous saves were kept as a safety backup ({safety.CreatedAt:HH:mm}).");
        }
        catch (Exception exception)
        {
            _showToast($"The save restore failed: {exception.Message}");
        }
    }

    /// <summary>Deletes one backup snapshot (a redundant copy — the live saves are untouched).</summary>
    [RelayCommand]
    private async Task DeleteBackupAsync(SaveBackupRowViewModel? row)
    {
        if (Game is not { } game || row is null)
        {
            return;
        }
        try
        {
            var deleted = await Task.Run(() => _saveBackups.DeleteBackup(game.Id, row.Backup));
            RefreshSaves();
            if (deleted)
            {
                _showToast("Backup snapshot deleted. The current saves were not touched.");
            }
        }
        catch (Exception exception)
        {
            _showToast($"The backup couldn't be deleted: {exception.Message}");
        }
    }

    /// <summary>Collection chips for the shown game; toggling edits membership.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CollectionChip> CollectionChips { get; private set; } =
        Array.Empty<CollectionChip>();

    public bool HasCollectionChips => CollectionChips.Count > 0;

    /// <summary>Rebuilds the membership chips (after toggles, edits, imports, removals).</summary>
    public void RefreshCollections()
    {
        CollectionChips = Game is { } game
            ? _loadCollectionChips(game.Id)
            : Array.Empty<CollectionChip>();
        Collections = Game is { } g ? _loadCollectionNames(g.Id) : Array.Empty<string>();
        OnPropertyChanged(nameof(HasCollectionChips));
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(CollectionsText));
    }

    [RelayCommand]
    private Task SetCoverArt() => Game is { } game ? _setCoverArt(game) : Task.CompletedTask;

    [RelayCommand]
    private Task RemoveCoverArt() => Game is { } game ? _removeCoverArt(game) : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleCollectionAsync(CollectionChip? chip)
    {
        if (Game is not { } game || chip is null)
        {
            return Task.CompletedTask;
        }
        return _toggleCollection(game.Id, chip.Id, !chip.IsMember);
    }
    private static string HumanizeDay(DateTime when)
    {
        var days = (int)(DateTime.Now.Date - when.Date).TotalDays;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 7 => $"{days} days ago",
            _ => when.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
