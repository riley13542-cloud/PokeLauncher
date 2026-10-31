using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Data;
using PokeLauncher.Models;
using PokeLauncher.Services;
using PokeLauncher.Views;

namespace PokeLauncher.ViewModels;

/// <summary>
/// Shell view model: owns all pages, sidebar navigation, the global search field,
/// back navigation for detail pages, and toast feedback. Also persists library state
/// (favorites/play state/metadata) to the local database whenever it changes, restores
/// saved appearance settings at startup, and orchestrates the ROM import, game editing,
/// and emulator-launch flows that the pages and dialogs drive.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AppDatabase _database;
    private readonly GameDetailsViewModel _gameDetails;
    private readonly RomImportService _imports;
    private readonly GameLauncherService _launcher;
    private readonly SaveBackupService _saveBackups = new();
    private readonly Dictionary<string, GameViewModel> _gamesById;
    private PageViewModelBase? _lastSidebarPage;
    private int _toastGeneration;

    public MainViewModel(AppDatabase database)
    {
        _database = database;
        _imports = new RomImportService();
        _launcher = new GameLauncherService(database);
        _saveBackups.Retention = ParseRetention(database.GetSetting("saves.retention"));
        CoverArtMode.Set(database.GetSetting("ui.coverArt") != "placeholders");
        try
        {
            Directory.CreateDirectory(ManagedLibrary.InboxRoot);
        }
        catch { /* the inbox scan reports if the folder is unusable */ }
        _launcher.SessionStarted += OnSessionStarted;
        _launcher.SessionEnded += OnSessionEnded;

        var games = database.LoadGames()
            .Select(m => new GameViewModel(m, OpenGame))
            .ToArray();
        foreach (var game in games)
        {
            game.PropertyChanged += OnGamePropertyChanged;
        }
        _gamesById = games.ToDictionary(g => g.Id, StringComparer.Ordinal);

        var savedBookmarks = database.LoadHackBookmarks();
        var catalogGames = games.ToArray();
        var hacks = database.LoadRomHacks()
            .Select(model =>
            {
                var viewModel = new RomHackViewModel(model, OpenHack);
                ResolveBaseGame(viewModel, catalogGames);
                if (savedBookmarks.TryGetValue(viewModel.Id, out var bookmarked))
                {
                    viewModel.IsBookmarked = bookmarked;
                }
                viewModel.PropertyChanged += OnHackPropertyChanged;
                return viewModel;
            })
            .ToList();
        var hackList = new ObservableCollection<RomHackViewModel>(hacks);

        Stats = new LibraryStatsViewModel(games, hackList);
        Library = new LibraryViewModel(games, RequestImportAsync, RunInboxScanAsync, RunFolderImportAsync);
        HackBrowser = new HackBrowserViewModel(
            hackList,
            catalogGames.Select(g => g.Title).ToList(),
            CreateHackAsync,
            async hack => await EditHackAsync(hack),
            OpenHack);
        Favorites = new FavoritesViewModel(games);
        RecentlyPlayed = new RecentlyPlayedViewModel(games);
        Collections = new CollectionsViewModel(
            database.LoadCollections(),
            games,
            CreateCollectionAsync,
            EditCollectionAsync);
        EmulatorDownloads = new EmulatorDownloadService(database, new EmulatorCatalog());
        Settings = new SettingsViewModel(
            database,
            EmulatorDownloads,
            ApplyAccent,
            value => Library.IsGridView = value,
            retention => _saveBackups.Retention = retention,
            ApplyCoverArtMode);
        About = new AboutViewModel();
        Home = new HomeViewModel(
            games, hackList, Stats,
            OpenGame,
            () => NavigateTo(Library),
            () => NavigateTo(HackBrowser),
            PlayGameAsync);
        _gameDetails = new GameDetailsViewModel(
            ShowToast,
            PlayGameAsync,
            ImportRomForAsync,
            EditGameAsync,
            () => NavigateTo(Settings),
            gameId => database.LoadPlaySessionsForGame(gameId),
            gameId => database.CountPlaySessions(gameId),
            gameId => database.LoadCollectionNamesForGame(gameId),
            BuildCollectionChips,
            _saveBackups,
            SetCoverArtAsync,
            RemoveCoverArtAsync,
            ToggleCollectionAsync);
        HackDetails = new RomHackDetailsViewModel(
            ShowToast,
            PlayHackAsync,
            EditHackAsync,
            () => NavigateTo(Settings));

        SidebarNav = new NavItemViewModel[]
        {
            new(Home, NavigateTo),
            new(Library, NavigateTo),
            new(HackBrowser, NavigateTo),
            new(Favorites, NavigateTo),
            new(RecentlyPlayed, NavigateTo),
            new(Collections, NavigateTo),
        };
        FooterNav = new NavItemViewModel[]
        {
            new(Settings, NavigateTo),
            new(About, NavigateTo),
        };

        _selectedPage = Home;
        _lastSidebarPage = Home;
        ApplySavedDefaultView();
        SyncNavSelection();
        BrandLogo = ManagedLibrary.LoadLogo();
        NotifyInboxIfPending();
        NotifyUpdateIfPending();
    }

    /// <summary>
    /// Startup update prompt: when the configured update source reports a newer version,
    /// the user gets a toast pointing at About → Check for updates (which downloads and
    /// verifies it). Silent when no source is configured or the check fails.
    /// </summary>
    private async void NotifyUpdateIfPending()
    {
        try
        {
            var updates = new UpdateService();
            if (!updates.IsConfigured)
            {
                return;
            }
            var result = await updates.CheckForUpdatesAsync(About.Version);
            if (result.Success && result.UpdateAvailable)
            {
                ShowToast($"{result.Message} Open About → Check for updates to get it.");
            }
        }
        catch
        {
            /* an unreachable update source must never break startup */
        }
    }

    /// <summary>Applies the cover-art preference to every live game view model.</summary>
    private void ApplyCoverArtMode()
    {
        foreach (var game in _gamesById.Values)
        {
            game.RefreshCover();
        }
    }


    private static int ParseRetention(string? value) =>
        int.TryParse(value, out var parsed) && parsed is >= 1 and <= 100 ? parsed : SaveBackupService.DefaultBackupsPerGame;

    /// <summary>Startup hint: the user dropped ROM files into the managed inbox.</summary>
    private void NotifyInboxIfPending()
    {
        try
        {
            if (!Directory.Exists(ManagedLibrary.InboxRoot))
            {
                return;
            }
            var pending = Directory.GetFiles(ManagedLibrary.InboxRoot)
                .Count(file => RomImportService.KnownExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));
            if (pending > 0)
            {
                ShowToast($"{pending} game file{(pending == 1 ? "" : "s")} are waiting in your ROM inbox — open My Library and use Check ROM inbox.");
            }
        }
        catch
        {
            /* a missing inbox is fine */
        }
    }

    /// <summary>Restores the saved default library view mode ("grid" or "list").</summary>
    private void ApplySavedDefaultView()
    {
        var saved = _database.GetSetting("library.gridView");
        if (saved is not null)
        {
            Library.IsGridView = saved != "list";
        }
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GameViewModel game)
        {
            return;
        }
        switch (e.PropertyName)
        {
            case nameof(GameViewModel.IsFavorite):
                _database.SaveGameFavorite(game.Id, game.IsFavorite);
                break;
            case nameof(GameViewModel.LastPlayed):
            case nameof(GameViewModel.PlaytimeHours):
                _database.SaveGamePlayState(game.Id, game.LastPlayed, game.PlaytimeHours);
                // Pages that surface play data re-sort/refill; shared cards update via bindings.
                RecentlyPlayed.Refresh();
                Library.Refresh();
                Stats.Refresh();
                Home.RefreshPlayData();
                break;
        }
    }

    private void OnHackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is RomHackViewModel hack && e.PropertyName == nameof(RomHackViewModel.IsBookmarked))
        {
            _database.SaveHackBookmark(hack.Id, hack.IsBookmarked);
        }
    }

    public HomeViewModel Home { get; }

    public LibraryViewModel Library { get; }

    public HackBrowserViewModel HackBrowser { get; }

    public FavoritesViewModel Favorites { get; }

    public RecentlyPlayedViewModel RecentlyPlayed { get; }

    public CollectionsViewModel Collections { get; }

    /// <summary>On-demand official emulator downloads (Settings page drives it; a future
    /// first-run setup flow can reuse this).</summary>
    public EmulatorDownloadService EmulatorDownloads { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    public GameDetailsViewModel GameDetails => _gameDetails;

    public RomHackDetailsViewModel HackDetails { get; }

    public LibraryStatsViewModel Stats { get; }

    public IReadOnlyList<NavItemViewModel> SidebarNav { get; }

    public IReadOnlyList<NavItemViewModel> FooterNav { get; }

    /// <summary>The game launcher that starts emulator processes and tracks sessions.</summary>
    public GameLauncherService Launcher => _launcher;

    /// <summary>Wired from the window once it is open; used by the import file picker.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    /// <summary>Wired from the window once it is open; owner for modal dialogs.</summary>
    public Window? DialogOwner { get; set; }

    /// <summary>The brand logo shown in the sidebar (bundled emblem or user's logo.png).</summary>
    [ObservableProperty]
    public partial IImage? BrandLogo { get; set; }

    [ObservableProperty]
    private PageViewModelBase? _selectedPage;

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial string? ToastMessage { get; set; }

    public bool HasToast => ToastMessage is not null;

    public bool ShowBack => SelectedPage is GameDetailsViewModel or RomHackDetailsViewModel;

    public bool ShowRefresh => SelectedPage is IRefreshablePage;

    public void ShowToast(string message)
    {
        ToastMessage = message;
        var generation = ++_toastGeneration;
        _ = ClearToastLaterAsync(generation);
    }

    private async Task ClearToastLaterAsync(int generation)
    {
        await Task.Delay(5000);
        if (generation != _toastGeneration)
        {
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation == _toastGeneration)
            {
                ToastMessage = null;
            }
        });
    }

    private void NavigateTo(PageViewModelBase page)
    {
        if (page is GameDetailsViewModel or RomHackDetailsViewModel)
        {
            SelectedPage = page;
            return;
        }
        _lastSidebarPage = page;
        SelectedPage = page;
    }

    private void OpenGame(GameViewModel game)
    {
        _gameDetails.SetGame(game);
        NavigateTo(_gameDetails);
    }

    private void OpenHack(RomHackViewModel hack)
    {
        HackDetails.SetHack(hack);
        NavigateTo(HackDetails);
    }

    partial void OnSelectedPageChanged(PageViewModelBase? value)
    {
        SyncNavSelection();
        OnPropertyChanged(nameof(ShowBack));
        OnPropertyChanged(nameof(ShowRefresh));
        if (value is ISearchablePage searchable)
        {
            searchable.SearchQuery = SearchText;
        }
    }

    partial void OnSearchTextChanged(string? value)
    {
        if (SelectedPage is ISearchablePage searchable)
        {
            searchable.SearchQuery = value;
        }
    }

    partial void OnToastMessageChanged(string? value) => OnPropertyChanged(nameof(HasToast));

    private void SyncNavSelection()
    {
        foreach (var item in SidebarNav.Concat(FooterNav))
        {
            item.IsCurrent = ReferenceEquals(item.Page, SelectedPage);
        }
    }

    private void ApplyAccent(AccentOption option)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }
        app.Resources["AccentBrush"] = new SolidColorBrush(option.From);
        app.Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(0x29, option.From.R, option.From.G, option.From.B));
        app.Resources["AccentBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x66, option.From.R, option.From.G, option.From.B));
        app.Resources["AccentGradientBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(option.From, 0),
                new GradientStop(option.To, 1),
            },
        };
    }

    // ==================================================================== launching

    /// <summary>
    /// Launches a game through its configured emulator. Runs on the UI thread but never
    /// blocks: the save backup and process start are quick, and the session's exit is
    /// handled by events. A failed save backup warns but does not block the launch.
    /// </summary>
    private async Task PlayGameAsync(GameViewModel game)
    {
        if (game.IsRunning)
        {
            ShowToast($"{game.Title} is already running.");
            return;
        }

        // Snapshot the current save files (next to the ROM) before the emulator can
        // touch them. Nothing to back up on a first play — that's not an error.
        try
        {
            var backup = await Task.Run(() => _saveBackups.CreateBackup(game.Id, game.RomPath ?? string.Empty));
            if (backup is not null)
            {
                ShowToast($"Save backup created ({backup.Summary}).");
            }
        }
        catch (Exception exception)
        {
            ShowToast($"The save backup failed ({exception.Message}) — launching anyway. " +
                      "You can retry from the game's page once the emulator is closed.");
        }

        var attempt = _launcher.Launch(game.Snapshot());
        if (!attempt.Success)
        {
            if (ReferenceEquals(_gameDetails.Game, game))
            {
                _gameDetails.ShowLaunchError(attempt.Error, attempt.MissingEmulator);
            }
            ShowToast(attempt.Error ?? "The game could not be launched.");
            return;
        }

        if (ReferenceEquals(_gameDetails.Game, game))
        {
            _gameDetails.ClearLaunchError();
        }
        // Stamp "last played" immediately so Recently Played is honest even if the
        // launcher is closed before the emulator exits. The session duration is added
        // when the process exits. (Failed launches never reach this line.)
        game.ApplyPlayState(DateTime.Now, game.PlaytimeHours);
        ShowToast($"Launching {game.Title}…");
    }

    private void OnSessionStarted(GameLaunchSession session)
    {
        // Raised synchronously from the UI thread inside Launch.
        if (_gamesById.TryGetValue(session.GameId, out var game))
        {
            game.IsRunning = true;
        }
    }

    private void OnSessionEnded(GameLaunchSession session, GameSessionEnd end)
    {
        // Raised on a background thread when the emulator process exits.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_gamesById.TryGetValue(session.GameId, out var game))
            {
                return;
            }
            game.IsRunning = false;

            var safeSeconds = Math.Max(0, end.DurationSeconds);
            var totalHours = game.PlaytimeHours + safeSeconds / 3600.0;
            game.ApplyPlayState(DateTime.Now, totalHours);

            _database.InsertPlaySession(new PlaySessionRecord
            {
                GameId = game.Id,
                StartedAt = end.StartedAt,
                EndedAt = end.EndedAt,
                DurationSeconds = safeSeconds,
            });
            _gameDetails.ReloadActivity();

            if (end.ExitedWithinSeconds)
            {
                ShowToast($"{session.GameTitle}'s emulator closed almost immediately. " +
                          "If this keeps happening, test the emulator on its own or check its setup in Settings.");
            }
            else if (!end.ExitedCleanly)
            {
                ShowToast($"{session.GameTitle} closed unexpectedly (exit code {end.ExitCode}). Playtime was still recorded.");
            }
        });
    }

    // ==================================================================== importing

    /// <summary>Entry point behind My Library's "Import Games" button.</summary>
    private async Task RequestImportAsync()
    {
        if (StorageProvider is null)
        {
            ShowToast("The file dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose game files to import",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported game files")
                {
                    Patterns = RomImportService.KnownExtensions.Select(extension => "*" + extension).ToArray(),
                },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } },
            },
        });
        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Cast<string>()
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }
        await ImportFilesAsync(paths);
    }

    /// <summary>
    /// Reviews the picked files in the import dialog, then writes the confirmed rows:
    /// new games are inserted, catalog matches get their ROM path attached, and
    /// duplicates/unsupported files are reported without being imported.
    /// </summary>
    private async Task ImportFilesAsync(IReadOnlyList<string> paths)
    {
        if (DialogOwner is null)
        {
            ShowToast("The import dialog isn't available yet — wait for the window to finish loading.");
            return;
        }

        var currentGames = SnapshotGames();
        var analyses = paths
            .Select(path => _imports.AnalyzeFile(path, currentGames))
            .ToList();
        var dialogViewModel = new ImportGamesDialogViewModel(analyses);
        var dialog = new ImportGamesDialog { DataContext = dialogViewModel };
        var dialogOwner = DialogOwner;
        var confirmed = await dialog.ShowDialog<bool?>(dialogOwner);
        if (confirmed != true)
        {
            return;
        }

        var added = 0;
        var attached = 0;
        foreach (var row in dialogViewModel.ConfirmedRows())
        {
            if (row.Kind == RomImportKind.Attach && row.ExistingGame is not null)
            {
                var managedPath = await CopyIntoManagedLibraryAsync(row.FilePath, row.ExistingGame.Id);
                if (managedPath is null)
                {
                    continue;
                }
                _database.SaveGameRomPath(row.ExistingGame.Id, managedPath);
                if (_gamesById.TryGetValue(row.ExistingGame.Id, out var existing))
                {
                    existing.ApplyRomPath(managedPath);
                }
                await AttachSidecarCoverIfPresentAsync(row.FilePath, row.ExistingGame.Id);
                attached++;
            }
            else if (row.Kind == RomImportKind.New)
            {
                var model = _imports.BuildImportedGame(
                    row.EffectiveTitle, row.Platform, row.Generation, row.FilePath, row.IsArchive);
                var managedPath = await CopyIntoManagedLibraryAsync(row.FilePath, model.Id);
                if (managedPath is null)
                {
                    continue;
                }
                var stored = model.WithRomPath(managedPath);
                if (_database.AddImportedGame(stored))
                {
                    RegisterNewGame(stored);
                    await AttachSidecarCoverIfPresentAsync(row.FilePath, stored.Id);
                    added++;
                }
            }
        }

        var problems = new List<string>();
        if (added + attached == 0)
        {
            problems.Add("nothing was imported");
        }
        if (_database.LoadError is { } databaseError)
        {
            problems.Add("database error: " + databaseError);
        }

        var summaryParts = new List<string>();
        if (added > 0)
        {
            summaryParts.Add($"{added} game{(added == 1 ? "" : "s")} added");
        }
        if (attached > 0)
        {
            summaryParts.Add($"{attached} file{(attached == 1 ? "" : "s")} linked to existing games");
        }
        if (dialogViewModel.SkippedCount > 0)
        {
            summaryParts.Add($"{dialogViewModel.SkippedCount} skipped");
        }
        var summary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "nothing imported";
        var message = problems.Count > 0
            ? $"Import finished with problems ({summary}). " + string.Join(" ", problems)
            : $"Import complete: {summary}.";

        if (ReferenceEquals(SelectedPage, Library))
        {
            Library.StatusMessage = message;
        }
        ShowToast(message);
        if (added > 0 && !ReferenceEquals(SelectedPage, Library))
        {
            // Bring the fresh import into view so the result is obvious.
            NavigateTo(Library);
        }
    }

    /// <summary>
    /// Copies a picked ROM into the game's managed folder under PokeLauncher's data
    /// directory and returns the managed path (the picked original is never moved).
    /// Returns null (with a toast) when the copy fails, so the row is skipped honestly.
    /// </summary>
    private async Task<string?> CopyIntoManagedLibraryAsync(string sourcePath, string gameId)
    {
        try
        {
            return await Task.Run(() => ManagedLibrary.CopyIntoManagedLibrary(sourcePath, gameId));
        }
        catch (Exception exception)
        {
            ShowToast($"“{Path.GetFileName(sourcePath)}” couldn't be copied into the library: {exception.Message}");
            return null;
        }
    }

    /// <summary>Entry point behind the details page's "Import ROM" button: attaches one
    /// picked file to an existing game (or to the catalog game it clearly belongs to).</summary>
    private async Task ImportRomForAsync(GameViewModel game)
    {
        if (StorageProvider is null)
        {
            ShowToast("The file dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose the ROM file for {game.Title}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported game files")
                {
                    Patterns = RomImportService.KnownExtensions.Select(extension => "*" + extension).ToArray(),
                },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } },
            },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        var analysis = _imports.AnalyzeFile(path, SnapshotGames());
        switch (analysis.Kind)
        {
            case RomImportKind.Unsupported:
                ShowToast(analysis.Note ?? "That file can't be imported.");
                return;
            case RomImportKind.Duplicate:
                ShowToast($"That file is already in the library as “{analysis.ExistingGame?.Title}”.");
                return;
            case RomImportKind.Attach when analysis.ExistingGame is { } match && match.Id != game.Id:
                // The file clearly belongs to a different known game — link it there instead
                // of silently mislabeling this one.
                _database.SaveGameRomPath(match.Id, path);
                if (_gamesById.TryGetValue(match.Id, out var matchViewModel))
                {
                    matchViewModel.ApplyRomPath(path);
                }
                ShowToast($"That file looks like “{match.Title}”, so it was linked to that game instead.");
                return;
        }

        var managedPath = await CopyIntoManagedLibraryAsync(path, game.Id);
        if (managedPath is null)
        {
            return;
        }
        _database.SaveGameRomPath(game.Id, managedPath);
        game.ApplyRomPath(managedPath);
        await AttachSidecarCoverIfPresentAsync(path, game.Id);
        ShowToast($"ROM file linked to {game.Title}. Ready to play.");
    }

    /// <summary>Live snapshot of all games (for matching + duplicate detection).</summary>
    private IReadOnlyList<PokemonGame> SnapshotGames() =>
        _gamesById.Values.Select(game => game.Snapshot()).ToList();

    private GameViewModel RegisterNewGame(PokemonGame model)
    {
        var game = new GameViewModel(model, OpenGame);
        game.PropertyChanged += OnGamePropertyChanged;
        _gamesById[game.Id] = game;
        Library.AddGames(new[] { game });
        Stats.AddGames(new[] { game });
        RecentlyPlayed.AddGames(new[] { game });
        Favorites.AddGames(new[] { game });
        Home.AddGames(new[] { game });
        Collections.AddGames(new[] { game });
        return game;
    }

    // ==================================================================== editing

    /// <summary>Opens the edit dialog for a game and applies the confirmed changes.</summary>
    private async Task EditGameAsync(GameViewModel game)
    {
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var viewModel = new EditGameDialogViewModel(game);
        var dialog = new EditGameDialog { DataContext = viewModel };
        var result = await dialog.ShowDialog<object?>(DialogOwner);

        if (result is "remove")
        {
            RemoveGame(game);
            return;
        }
        if (result is "detach")
        {
            // Forget the ROM link; the managed copy (ours) is deleted, the original untouched.
            if (ManagedLibrary.IsManagedPath(game.RomPath))
            {
                var detachedId = game.Id;
                await Task.Run(() => ManagedLibrary.DeleteManagedRom(detachedId));
            }
            _database.SaveGameRomPath(game.Id, null);
            game.ApplyRomPath(null);
            ShowToast($"ROM file detached from {game.Title}. Your original file was not touched.");
            return;
        }
        if (result is not true)
        {
            return;
        }

        var model = game.Snapshot();
        var edited = model.WithMetadata(
            viewModel.Title.Trim(),
            viewModel.Platform,
            viewModel.Generation,
            viewModel.Description);
        _database.SaveGameMetadata(edited);
        if (_database.LoadError is { } error)
        {
            ShowToast("The changes couldn't be saved: " + error);
            return;
        }
        game.ApplyMetadata(edited.Title, edited.Platform, edited.Generation, edited.Description);
        ShowToast("Game details saved.");
    }

    /// <summary>
    /// If an image file sits next to the imported ROM with the same name
    /// (e.g. "Emerald.gba" + "Emerald.png"), it is copied in as the game's cover art.
    /// </summary>
    private async Task AttachSidecarCoverIfPresentAsync(string sourcePath, string gameId)
    {
        try
        {
            var directory = Path.GetDirectoryName(sourcePath);
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(stem))
            {
                return;
            }
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
            {
                var candidate = Path.Combine(directory, stem + extension);
                if (!File.Exists(candidate))
                {
                    continue;
                }
                await Task.Run(() => ManagedLibrary.SetCover(candidate, gameId));
                if (_gamesById.TryGetValue(gameId, out var game))
                {
                    game.RefreshCover();
                }
                ShowToast($"Cover art picked up from {Path.GetFileName(candidate)}.");
                return;
            }
        }
        catch
        {
            /* a broken sidecar image must not fail the import */
        }
    }

    // ==================================================================== collections

    /// <summary>Creates a new collection from the edit dialog's confirmed state.</summary>
    private async Task CreateCollectionAsync()
    {
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var viewModel = new EditCollectionDialogViewModel(null, CollectionsGamesSource());
        var dialog = new EditCollectionDialog { DataContext = viewModel };
        var result = await dialog.ShowDialog<object?>(DialogOwner);
        if (result is not true)
        {
            return;
        }

        var definition = new GameCollection
        {
            Id = "user-" + Guid.NewGuid().ToString("N")[..12],
            Name = viewModel.Name.Trim(),
            Description = viewModel.Description?.Trim() ?? string.Empty,
            GameIds = viewModel.Members.Where(m => m.IsMember).Select(m => m.Game.Id).ToList(),
        };
        if (!_database.AddCollection(definition))
        {
            ShowToast(_database.LoadError is { } error
                ? $"The collection couldn't be created: {error}"
                : "The collection couldn't be created.");
            return;
        }
        Collections.AddCollection(definition);
        _gameDetails.ReloadActivity();
        ShowToast($"Collection “{definition.Name}” created.");
    }

    /// <summary>Edits (rename/re-member/delete) a collection from the edit dialog.</summary>
    private async Task EditCollectionAsync(CollectionViewModel collection)
    {
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var viewModel = new EditCollectionDialogViewModel(collection, CollectionsGamesSource());
        var dialog = new EditCollectionDialog { DataContext = viewModel };
        var result = await dialog.ShowDialog<object?>(DialogOwner);

        if (result is "delete")
        {
            if (!_database.DeleteCollection(collection.Id))
            {
                ShowToast(_database.LoadError is { } error
                    ? $"The collection couldn't be deleted: {error}"
                    : "The collection couldn't be deleted.");
                return;
            }
            Collections.RemoveCollection(collection);
            _gameDetails.ReloadActivity();
            ShowToast($"Collection “{collection.Name}” was deleted. The games themselves are untouched.");
            return;
        }
        if (result is not true)
        {
            return;
        }

        var definition = new GameCollection
        {
            Id = collection.Id,
            Name = viewModel.Name.Trim(),
            Description = viewModel.Description?.Trim() ?? string.Empty,
            GameIds = viewModel.Members.Where(m => m.IsMember).Select(m => m.Game.Id).ToList(),
        };
        if (!_database.SaveCollection(definition))
        {
            ShowToast(_database.LoadError is { } error
                ? $"The collection couldn't be saved: {error}"
                : "The collection couldn't be saved.");
            return;
        }
        Collections.ApplyCollectionUpdate(definition);
        _gameDetails.ReloadActivity();
        ShowToast($"Collection “{definition.Name}” saved.");
    }

    /// <summary>The member editor works on the live game list (imports included).</summary>
    private IReadOnlyList<GameViewModel> CollectionsGamesSource() =>
        _gamesById.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();

    // ==================================================================== covers

    /// <summary>Picks an image and stores it as the game's custom cover art.</summary>
    private async Task SetCoverArtAsync(GameViewModel game)
    {
        if (StorageProvider is null)
        {
            ShowToast("The file dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose cover art for {game.Title}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" },
                },
            },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }
        try
        {
            await Task.Run(() => ManagedLibrary.SetCover(path, game.Id));
        }
        catch (Exception exception)
        {
            ShowToast($"The cover art couldn't be saved: {exception.Message}");
            return;
        }
        game.RefreshCover();
        ShowToast($"Cover art updated for {game.Title}.");
    }

    /// <summary>Removes the custom cover; the placeholder art is shown again.</summary>
    private async Task RemoveCoverArtAsync(GameViewModel game)
    {
        var removedId = game.Id;
        await Task.Run(() => ManagedLibrary.DeleteCover(removedId));
        game.RefreshCover();
        ShowToast($"Cover art removed for {game.Title}. The generated art is shown again.");
    }

    // ==================================================================== collections

    private IReadOnlyList<CollectionChip> BuildCollectionChips(string gameId) =>
        Collections.Collections
            .Select(collection => new CollectionChip(
                collection.Id,
                collection.Name,
                collection.Games.Any(member => member.Id == gameId)))
            .ToList();

    /// <summary>Toggles a game's membership in one collection (details-page chips).</summary>
    private async Task ToggleCollectionAsync(string gameId, string collectionId, bool member)
    {
        if (!_database.SetGameMembership(gameId, collectionId, member))
        {
            ShowToast(_database.LoadError is { } error
                ? $"The collection couldn't be updated: {error}"
                : "The collection couldn't be updated.");
            return;
        }
        Collections.SetGameMembership(gameId, collectionId, member);
        _gameDetails.RefreshCollections();
        var collection = Collections.Collections.FirstOrDefault(c => c.Id == collectionId);
        var name = collection?.Name ?? "the collection";
        ShowToast(member ? $"Added to \u201C{name}\u201D." : $"Removed from \u201C{name}\u201D.");
    }

    // ==================================================================== folder import

    /// <summary>
    /// One-step attach for people who keep their game files in a folder: pick the folder,
    /// PokeLauncher scans it, matches every file against the built-in catalog, and the
    /// review dialog attaches them all in one go. Files in the source folder are only
    /// ever read - nothing there is moved, modified, or deleted.
    /// </summary>
    private async Task RunFolderImportAsync()
    {
        if (StorageProvider is null)
        {
            ShowToast("The folder dialog isn't available yet - wait for the window to finish loading.");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder that contains your game files",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }
        if (!Directory.Exists(folder))
        {
            ShowToast("That folder can't be found.");
            return;
        }
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }

        var files = Directory.GetFiles(folder)
            .Where(file => RomImportService.KnownExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .ToList();
        if (files.Count == 0)
        {
            ShowToast($"No game files found in {folder}. Supported types: {RomImportService.SupportedExtensionsText}.");
            return;
        }

        var currentGames = SnapshotGames();
        var analyses = files
            .Select(file => _imports.AnalyzeFile(file, currentGames))
            .ToList();
        var dialogViewModel = new ImportGamesDialogViewModel(analyses);
        var dialog = new ImportGamesDialog { DataContext = dialogViewModel };
        var dialogOwner = DialogOwner;
        var confirmed = await dialog.ShowDialog<bool?>(dialogOwner);
        if (confirmed != true)
        {
            return;
        }

        var attached = 0;
        var added = 0;
        foreach (var row in dialogViewModel.ConfirmedRows())
        {
            if (row.Kind == RomImportKind.Attach && row.ExistingGame is not null)
            {
                var managedPath = await CopyIntoManagedLibraryAsync(row.FilePath, row.ExistingGame.Id);
                if (managedPath is null)
                {
                    continue;
                }
                _database.SaveGameRomPath(row.ExistingGame.Id, managedPath);
                if (_gamesById.TryGetValue(row.ExistingGame.Id, out var existing))
                {
                    existing.ApplyRomPath(managedPath);
                }
                await AttachSidecarCoverIfPresentAsync(row.FilePath, row.ExistingGame.Id);
                attached++;
            }
            else if (row.Kind == RomImportKind.New)
            {
                var model = _imports.BuildImportedGame(
                    row.EffectiveTitle, row.Platform, row.Generation, row.FilePath, row.IsArchive);
                var managedPath = await CopyIntoManagedLibraryAsync(row.FilePath, model.Id);
                if (managedPath is null)
                {
                    continue;
                }
                var stored = model.WithRomPath(managedPath);
                if (_database.AddImportedGame(stored))
                {
                    RegisterNewGame(stored);
                    await AttachSidecarCoverIfPresentAsync(row.FilePath, stored.Id);
                    added++;
                }
            }
        }

        var summaryParts = new List<string>();
        if (attached > 0) { summaryParts.Add($"{attached} game{(attached == 1 ? "" : "s")} ready to play"); }
        if (added > 0) { summaryParts.Add($"{added} new game{(added == 1 ? "" : "s")} added"); }
        if (dialogViewModel.SkippedCount > 0) { summaryParts.Add($"{dialogViewModel.SkippedCount} skipped"); }
        var summary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "nothing imported";
        if (ReferenceEquals(SelectedPage, Library))
        {
            Library.StatusMessage = $"Folder import complete: {summary}. Your files were not moved or modified.";
        }
        ShowToast($"Folder import complete: {summary}. Nothing in the folder was moved or modified.");
    }

    // ==================================================================== ROM inbox

    /// <summary>
    /// Matches ROM files the user dropped into the managed inbox against the library.
    /// Confirmed matches are copied into managed storage; the inbox files themselves stay
    /// until the user deletes them (PokeLauncher never deletes originals).
    /// </summary>
    private async Task RunInboxScanAsync()
    {
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        if (!Directory.Exists(ManagedLibrary.InboxRoot))
        {
            ShowToast($"The ROM inbox folder doesn't exist yet: {ManagedLibrary.InboxRoot}");
            return;
        }
        var files = Directory.GetFiles(ManagedLibrary.InboxRoot)
            .Where(file => RomImportService.KnownExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .ToList();
        if (files.Count == 0)
        {
            ShowToast($"The ROM inbox is empty. Drop your own game files into {ManagedLibrary.InboxRoot} and check again.");
            return;
        }

        var currentGames = SnapshotGames();
        var analyses = files
            .Select(file => _imports.AnalyzeFile(file, currentGames))
            .ToList();
        var dialogViewModel = new ImportGamesDialogViewModel(analyses);
        var dialog = new ImportGamesDialog { DataContext = dialogViewModel };
        var dialogOwner = DialogOwner;
        var confirmed = await dialog.ShowDialog<bool?>(dialogOwner);
        if (confirmed != true)
        {
            return;
        }

        var attached = 0;
        var added = 0;
        foreach (var row in dialogViewModel.ConfirmedRows())
        {
            if (row.Kind == RomImportKind.Attach && row.ExistingGame is not null)
            {
                var managedPath = await CopyIntoManagedLibraryAsync(row.FilePath, row.ExistingGame.Id);
                if (managedPath is null)
                {
                    continue;
                }
                _database.SaveGameRomPath(row.ExistingGame.Id, managedPath);
                if (_gamesById.TryGetValue(row.ExistingGame.Id, out var existing))
                {
                    existing.ApplyRomPath(managedPath);
                }
                await AttachSidecarCoverIfPresentAsync(row.FilePath, row.ExistingGame.Id);
                attached++;
            }
            else if (row.Kind == RomImportKind.New)
            {
                var model = _imports.BuildImportedGame(
                    row.EffectiveTitle, row.Platform, row.Generation, row.FilePath, row.IsArchive);
                var managedPath = await CopyIntoManagedLibraryAsync(row.FilePath, model.Id);
                if (managedPath is null)
                {
                    continue;
                }
                var stored = model.WithRomPath(managedPath);
                if (_database.AddImportedGame(stored))
                {
                    RegisterNewGame(stored);
                    await AttachSidecarCoverIfPresentAsync(row.FilePath, stored.Id);
                    added++;
                }
            }
        }

        var summaryParts = new List<string>();
        if (added > 0) { summaryParts.Add($"{added} game{(added == 1 ? "" : "s")} added"); }
        if (attached > 0) { summaryParts.Add($"{attached} game{(attached == 1 ? "" : "s")} ready to play"); }
        if (dialogViewModel.SkippedCount > 0) { summaryParts.Add($"{dialogViewModel.SkippedCount} skipped"); }
        var summary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "nothing imported";
        ShowToast($"Inbox check complete: {summary}. The inbox files stay until you delete them.");
    }

    // ==================================================================== ROM hacks

    private void ResolveBaseGame(RomHackViewModel hack, IReadOnlyList<GameViewModel> catalogGames)
    {
        var baseGame = catalogGames.FirstOrDefault(g => g.Id == hack.BaseGameId);
        hack.BaseGameTitle = baseGame?.Title ?? "Unknown base game";
        hack.Platform = baseGame?.Platform ?? string.Empty;
        hack.Generation = baseGame?.Generation ?? 0;
    }

    private void RefreshHackBaseGame(RomHackViewModel hack)
    {
        if (_gamesById.TryGetValue(hack.BaseGameId, out var baseGame))
        {
            hack.BaseGameTitle = baseGame.Title;
            hack.Platform = baseGame.Platform;
            hack.Generation = baseGame.Generation;
        }
    }

    /// <summary>Creates a hack from the edit dialog, copying the patched ROM into managed storage.</summary>
    private async Task CreateHackAsync()
    {
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var catalogGames = _gamesById.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
        var viewModel = new EditHackDialogViewModel(null, catalogGames);
        viewModel.BrowseRomRequested += vm => _ = BrowseHackRomAsync(vm);
        var dialog = new EditHackDialog { DataContext = viewModel };
        var result = await dialog.ShowDialog<object?>(DialogOwner);
        if (result is "delete" or not true)
        {
            return;
        }

        var id = "hack-" + Guid.NewGuid().ToString("N")[..12];
        string? managedPath = null;
        if (!string.IsNullOrWhiteSpace(viewModel.RomPath))
        {
            managedPath = await CopyIntoManagedLibraryAsync(viewModel.RomPath, id);
        }
        var model = viewModel.BuildModel(id, DateTime.Now, managedPath);
        if (!_database.SaveRomHack(model))
        {
            ShowToast(_database.LoadError is { } error
                ? $"The hack couldn't be saved: {error}"
                : "The hack couldn't be saved.");
            return;
        }

        var hackViewModel = new RomHackViewModel(model, OpenHack);
        ResolveBaseGame(hackViewModel, catalogGames);
        HackBrowser.AddHack(hackViewModel);
        ShowToast($"ROM hack \u201C{model.Title}\u201D added.");
    }

    /// <summary>Edits or deletes a hack from the edit dialog.</summary>
    private async Task EditHackAsync(RomHackViewModel hack)
    {
        if (DialogOwner is null)
        {
            ShowToast("The dialog isn't available yet — wait for the window to finish loading.");
            return;
        }
        var catalogGames = _gamesById.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
        var viewModel = new EditHackDialogViewModel(hack, catalogGames);
        viewModel.BrowseRomRequested += vm => _ = BrowseHackRomAsync(vm);
        var dialog = new EditHackDialog { DataContext = viewModel };
        var result = await dialog.ShowDialog<object?>(DialogOwner);

        if (result is "delete")
        {
            if (!_database.DeleteRomHack(hack.Id))
            {
                ShowToast(_database.LoadError is { } error
                    ? $"The hack couldn't be deleted: {error}"
                    : "The hack couldn't be deleted.");
                return;
            }
            _database.SaveHackBookmark(hack.Id, false);
            if (ManagedLibrary.IsManagedPath(hack.RomPath))
            {
                var deletedId = hack.Id;
                await Task.Run(() => ManagedLibrary.DeleteManagedRom(deletedId));
            }
            HackBrowser.RemoveHack(hack);
            ShowToast($"ROM hack \u201C{hack.Title}\u201D was deleted. The patched ROM file on disk was not touched.");
            return;
        }
        if (result is not true)
        {
            return;
        }

        var resolvedPath = hack.RomPath;
        if (!string.IsNullOrWhiteSpace(viewModel.RomPath) &&
            !string.Equals(viewModel.RomPath, hack.RomPath, StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath = await CopyIntoManagedLibraryAsync(viewModel.RomPath, hack.Id);
        }
        var model = viewModel.BuildModel(hack.Id, hack.CreatedAt, resolvedPath);
        if (!_database.SaveRomHack(model))
        {
            ShowToast(_database.LoadError is { } error
                ? $"The hack couldn't be saved: {error}"
                : "The hack couldn't be saved.");
            return;
        }
        hack.ApplyUpdate(model);
        RefreshHackBaseGame(hack);
        ShowToast($"ROM hack \u201C{model.Title}\u201D saved.");
    }

    /// <summary>File picker for a hack's patched ROM (wired to the edit dialog).</summary>
    private async Task BrowseHackRomAsync(EditHackDialogViewModel viewModel)
    {
        if (StorageProvider is null)
        {
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the patched ROM file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported game files")
                {
                    Patterns = RomImportService.KnownExtensions.Select(extension => "*" + extension).ToArray(),
                },
            },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            viewModel.SetRomPath(path);
        }
    }

    /// <summary>Launches a hack's patched ROM through the base game's platform emulator.</summary>
    private async Task PlayHackAsync(RomHackViewModel hack)
    {
        if (hack.IsRunning)
        {
            ShowToast($"{hack.Title} is already running.");
            return;
        }
        if (!_gamesById.TryGetValue(hack.BaseGameId, out var baseGame))
        {
            ShowToast($"The base game for {hack.Title} is missing from the library.");
            return;
        }

        try
        {
            var backup = await Task.Run(() => _saveBackups.CreateBackup(hack.Id, hack.RomPath ?? string.Empty));
            if (backup is not null)
            {
                ShowToast($"Save backup created ({backup.Summary}).");
            }
        }
        catch (Exception exception)
        {
            ShowToast($"The save backup failed ({exception.Message}) — launching anyway.");
        }

        var hackGame = new PokemonGame
        {
            Id = hack.Id,
            Title = hack.Title,
            Generation = baseGame.Generation,
            Platform = baseGame.Platform,
            ReleaseYear = baseGame.ReleaseYear,
            Description = hack.Description,
            CoverFromHex = baseGame.CoverFrom.ToString(),
            CoverToHex = baseGame.CoverTo.ToString(),
            RomPath = hack.RomPath,
        };
        var attempt = _launcher.Launch(hackGame);
        if (!attempt.Success)
        {
            if (ReferenceEquals(HackDetails.Hack, hack))
            {
                HackDetails.ShowLaunchError(attempt.Error, attempt.MissingEmulator);
            }
            ShowToast(attempt.Error ?? "The hack could not be launched.");
            return;
        }

        if (ReferenceEquals(HackDetails.Hack, hack))
        {
            HackDetails.ClearLaunchError();
        }
        ShowToast($"Launching {hack.Title}…");
    }

    private void RemoveGame(GameViewModel game)
    {
        if (!_database.DeleteGame(game.Id))
        {
            ShowToast(_database.LoadError is { } error
                ? $"{game.Title} couldn't be removed: {error}"
                : $"{game.Title} couldn't be removed.");
            return;
        }
        // PokeLauncher-managed data goes with the entry: the managed ROM copy and the
        // game's save-backup snapshots. The original imported file is never touched.
        var gameId = game.Id;
        var hadManagedRom = ManagedLibrary.IsManagedPath(game.RomPath);
        Task.Run(() =>
        {
            if (hadManagedRom)
            {
                ManagedLibrary.DeleteManagedRom(gameId);
            }
            try
            {
                var saves = Path.Combine(ManagedLibrary.SavesRoot, gameId);
                if (Directory.Exists(saves))
                {
                    Directory.Delete(saves, recursive: true);
                }
            }
            catch { /* best effort; leftovers are harmless */ }
        });
        game.PropertyChanged -= OnGamePropertyChanged;
        _gamesById.Remove(game.Id);
        Library.RemoveGame(game);
        Stats.RemoveGame(game);
        RecentlyPlayed.RemoveGame(game);
        Favorites.RemoveGame(game);
        Home.RemoveGame(game);
        Collections.RemoveGame(game);
        if (ReferenceEquals(_gameDetails.Game, game))
        {
            _gameDetails.SetGame(null);
            NavigateTo(Library);
        }
        ShowToast(hadManagedRom
            ? $"{game.Title} was removed from your library, including PokeLauncher's managed copy and save backups. Your original file was not touched."
            : $"{game.Title} was removed from your library. The ROM file on disk was not touched.");
    }

    // ==================================================================== shell commands

    [RelayCommand]
    private void GoBack()
    {
        if (_lastSidebarPage is { } page)
        {
            NavigateTo(page);
        }
        else
        {
            SelectedPage = Home;
        }
    }

    [RelayCommand]
    private void OpenSettings() => NavigateTo(Settings);

    [RelayCommand]
    private void Refresh()
    {
        if (SelectedPage is IRefreshablePage refreshable)
        {
            refreshable.Refresh();
            ShowToast("View refreshed.");
        }
    }
}
