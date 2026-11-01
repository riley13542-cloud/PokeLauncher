using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Models;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>A save-backup retention choice (snapshots kept per game).</summary>
public sealed record BackupRetentionOption(int Value);

/// <summary>An accent-color choice for the Settings appearance section.</summary>
public sealed partial class AccentOption : ViewModelBase
{
    public AccentOption(string name, string fromHex, string toHex)
    {
        Name = name;
        From = Color.Parse(fromHex);
        To = Color.Parse(toHex);
        FromBrush = new SolidColorBrush(From);
        AutomationName = "Accent " + name;
    }

    public string Name { get; }

    public Color From { get; }

    public Color To { get; }

    public IBrush FromBrush { get; }

    public string AutomationName { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Settings row for one emulator system; edits write through to the database. Also owns
/// the on-demand "download the official build" flow for the emulator covering this system:
/// consent panel → checksum-verified download → auto-configuration, plus removal.
/// </summary>
public sealed partial class EmulatorSystemViewModel : ViewModelBase
{
    private readonly AppDatabase _database;
    private readonly EmulatorDownloadService _downloads;
    private readonly Action<string>? _notifyStatus;
    private readonly EmulatorManifestEntry? _entry;
    private EmulatorDownloadRecord? _record;
    private bool _initializing = true;
    private bool _suppressPersist;
    private CancellationTokenSource? _installCancellation;

    /// <summary>Raised when the user asks to edit this system's keyboard controls
    /// (MainViewModel opens the controls dialog).</summary>
    public event Action<EmulatorSystemViewModel>? ControlsRequested;

    public EmulatorSystemViewModel(
        AppDatabase database,
        EmulatorDownloadService downloads,
        EmulatorSystem system,
        EmulatorConfig config,
        Action<string>? notifyStatus = null)
    {
        _database = database;
        _downloads = downloads;
        _notifyStatus = notifyStatus;
        Key = system.Key;
        DisplayName = system.DisplayName;
        FileHint = system.FileHint;
        _entry = downloads.GetEntryForSystem(system.Key);
        _record = downloads.GetRecordForSystem(system.Key);
        ExecutablePath = config.ExecutablePath;
        Arguments = config.Arguments;
        _initializing = false;
    }

    /// <summary>Raised after this system's emulator install/removal changes, so sibling
    /// rows covering the same emulator (GB/GBC/GBA → mGBA) can refresh themselves.</summary>
    public event Action? InstallChanged;

    /// <summary>Stable system identifier, e.g. "GBA" ("GB", "GBC", "GBA", "NDS").</summary>
    public string Key { get; }

    public string DisplayName { get; }

    /// <summary>Hint shown next to the system name, e.g. ".gba, .zip".</summary>
    public string FileHint { get; }

    [ObservableProperty]
    public partial string ExecutablePath { get; set; }

    [ObservableProperty]
    public partial string Arguments { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial double DownloadPercent { get; private set; }

    [ObservableProperty]
    public partial string? InstallStage { get; private set; }

    [ObservableProperty]
    public partial string? InstallError { get; private set; }

    [ObservableProperty]
    public partial bool IsConfirmOpen { get; private set; }

    public bool HasInstallError => InstallError is not null;

    /// <summary>True when a PokeLauncher-managed install is recorded for this system's emulator (current or broken).</summary>
    public bool HasInstallRecord =>
        _record is not null && !IsBusy &&
        _entry is not null && _record.Version == _entry.Version;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExecutablePath);

    /// <summary>Manifest entry covering this system; null when no official download is offered.</summary>
    public EmulatorManifestEntry? Entry => _entry;

    public bool HasDownloadOption => _entry is not null && !string.IsNullOrWhiteSpace(_entry.Download.Url);

    public string DownloadName => _entry is null ? string.Empty : $"{_entry.Name} {_entry.Version}";

    /// <summary>Distinguishes the otherwise-identical per-system buttons for screen readers.</summary>
    public string DownloadButtonAutomationName => $"Download official build for {DisplayName}";

    public string ConfirmDownloadAutomationName => $"Confirm download for {DisplayName}";

    public string RemoveButtonAutomationName => $"Remove installed build for {DisplayName}";

    /// <summary>One-line honest consent summary shown before any download starts.</summary>
    public string DownloadSummary
    {
        get
        {
            if (_entry is null)
            {
                return string.Empty;
            }
            var size = _entry.Download.SizeBytes > 0
                ? FormatMegabytes(_entry.Download.SizeBytes)
                : "unknown size";
            var host = Uri.TryCreate(_entry.Download.Url, UriKind.Absolute, out var url)
                ? url.Host
                : _entry.SourceUrl;
            return $"≈ {size} · {_entry.License} · downloaded from {host}";
        }
    }

    /// <summary>Shown when the manifest itself carries a caveat (e.g. DS firmware notes).</summary>
    public string? EmulatorNote => _entry?.Note;

    public bool HasEmulatorNote => !string.IsNullOrWhiteSpace(EmulatorNote);

    /// <summary>The manifest version is installed on disk and nothing needs a refresh.</summary>
    public bool IsUpToDate =>
        _record is not null && _entry is not null &&
        _record.Version == _entry.Version &&
        File.Exists(_record.ExecutablePath);

    /// <summary>Download/update button currently makes sense (idle, not already current).</summary>
    public bool CanOfferDownload => HasDownloadOption && !IsBusy && !IsConfirmOpen && !IsUpToDate;

    /// <summary>Chip shown when the configured executable comes from a PokeLauncher-managed install.</summary>
    public bool HasOfficialChip =>
        _record is not null && !IsBusy && ExecutablePath != null &&
        ExecutablePath.StartsWith(_record.InstallFolder, StringComparison.OrdinalIgnoreCase);

    public string OfficialChipText => _record is null ? string.Empty : $"Official v{_record.Version}";

    public string InstallStatusText
    {
        get
        {
            if (IsBusy)
            {
                return InstallStage ?? "Working…";
            }
            if (_entry is null)
            {
                return string.Empty;
            }
            if (_record is null)
            {
                return $"{_entry.Name} {_entry.Version} is available as a checksum-verified download straight from its official project.";
            }
            if (!File.Exists(_record.ExecutablePath))
            {
                return $"The recorded {_entry.Name} install is missing its files — download again, or browse for your own copy.";
            }
            return _record.Version == _entry.Version
                ? $"v{_record.Version} installed in {_record.InstallFolder}"
                : $"v{_record.Version} installed — v{_entry.Version} is available.";
        }
    }

    partial void OnExecutablePathChanged(string value)
    {
        if (_initializing || _suppressPersist)
        {
            RefreshComputed();
            return;
        }
        _database.SaveEmulatorConfig(Key, value, Arguments);
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(SupportsFullscreen));
        OnPropertyChanged(nameof(FullscreenEnabled));
        RefreshComputed();
    }

    partial void OnArgumentsChanged(string value)
    {
        if (_initializing || _suppressPersist)
        {
            return;
        }
        _database.SaveEmulatorConfig(Key, ExecutablePath, value);
        OnPropertyChanged(nameof(FullscreenEnabled));
    }

    partial void OnIsBusyChanged(bool value) => RefreshComputed();

    partial void OnIsConfirmOpenChanged(bool value) => RefreshComputed();

    partial void OnInstallStageChanged(string? value) => OnPropertyChanged(nameof(InstallStatusText));

    partial void OnInstallErrorChanged(string? value) => OnPropertyChanged(nameof(HasInstallError));

    private void RefreshComputed()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(CanOfferDownload));
        OnPropertyChanged(nameof(HasOfficialChip));
        OnPropertyChanged(nameof(OfficialChipText));
        OnPropertyChanged(nameof(HasInstallRecord));
        OnPropertyChanged(nameof(InstallStatusText));
    }

    /// <summary>Re-reads install record + config from the database (e.g. after a sibling
    /// system installed the same emulator). Never writes; only displays.</summary>
    public void RefreshInstallState()
    {
        _record = _downloads.GetRecordForSystem(Key);
        var config = _database.LoadEmulatorConfigs().TryGetValue(Key, out var value) ? value : null;
        if (config is not null)
        {
            _suppressPersist = true;
            ExecutablePath = config.ExecutablePath;
            Arguments = config.Arguments;
            _suppressPersist = false;
        }
        RefreshComputed();
    }

    // ------------------------------------------------------------ download flow

    [RelayCommand]
    private void ShowDownloadConsent()
    {
        if (IsBusy || !HasDownloadOption)
        {
            return;
        }
        InstallError = null;
        IsConfirmOpen = true;
    }

    [RelayCommand]
    private void DeclineDownloadConsent() => IsConfirmOpen = false;

    [RelayCommand]
    private void CancelInstall() => _installCancellation?.Cancel();

    [RelayCommand]
    private async Task ConfirmDownloadAsync()
    {
        if (_entry is null || IsBusy)
        {
            return;
        }
        InstallError = null;
        IsConfirmOpen = false;
        IsBusy = true;
        DownloadPercent = 0;
        InstallStage = "Starting…";
        _installCancellation = new CancellationTokenSource();

        // Progress<T> posts to the Avalonia UI thread context captured here.
        var progress = new Progress<InstallProgress>(p =>
        {
            DownloadPercent = p.Percent;
            InstallStage = p.Detail is null ? p.Stage : $"{p.Stage}… {p.Detail}";
        });

        try
        {
            var outcome = await _downloads.InstallAsync(_entry, progress, _installCancellation.Token);
            if (outcome.Success)
            {
                ApplyInstallOutcome(outcome);
                _notifyStatus?.Invoke(
                    $"{_entry.Name} {_entry.Version} verified, installed, and configured for " +
                    $"{string.Join(", ", SystemDisplayNames(_entry))}.");
            }
            else
            {
                InstallError = outcome.Error;
                _notifyStatus?.Invoke(outcome.Error ?? "Download failed.");
            }
        }
        finally
        {
            _installCancellation?.Dispose();
            _installCancellation = null;
            InstallStage = null;
            IsBusy = false;
            RefreshComputed();
            // Sibling rows for the same emulator (GB/GBC/GBA -> mGBA) re-read the database
            // so their status stays honest after a shared install or a failed attempt.
            InstallChanged?.Invoke();
        }
    }

    private void ApplyInstallOutcome(InstallOutcome outcome)
    {
        _suppressPersist = true;
        if (!string.IsNullOrEmpty(outcome.ExecutablePath))
        {
            ExecutablePath = outcome.ExecutablePath;
        }
        if (_entry is not null)
        {
            Arguments = _entry.Download.LaunchArguments;
        }
        _suppressPersist = false;
        // The service already persisted this; repeat-insist so the row stays in sync even
        // if the property values happened to be unchanged.
        _database.SaveEmulatorConfig(Key, ExecutablePath, Arguments);
        _record = _downloads.GetRecordForEmulatorId(_entry?.Id);
        OnPropertyChanged(nameof(IsConfigured));
        RefreshComputed();
    }

    [RelayCommand]
    private void RemoveInstall()
    {
        if (IsBusy || _entry is null)
        {
            return;
        }
        var message = _downloads.RemoveInstall(Key);
        _record = _downloads.GetRecordForEmulatorId(_entry.Id);
        RefreshInstallState();
        InstallError = null;
        InstallChanged?.Invoke();
        _notifyStatus?.Invoke(message);
    }

    private IEnumerable<string> SystemDisplayNames(EmulatorManifestEntry entry)
    {
        var byKey = EmulatorSystems.All.ToDictionary(s => s.Key, s => s.DisplayName, StringComparer.OrdinalIgnoreCase);
        return entry.Systems.Select(k => byKey.TryGetValue(k, out var name) ? name : k);
    }

    private static string FormatMegabytes(long bytes) =>
        (bytes / 1024.0 / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB";

    // ------------------------------------------------------------ launch options

    /// <summary>Whether the configured emulator is known to PokeLauncher, so launch-option
    /// toggles (like fullscreen) can be offered. Unknown emulators use manual arguments.</summary>
    public bool SupportsFullscreen => EmulatorLaunchFlags.FullscreenFlagFor(ExecutablePath) is not null;

    /// <summary>True when the configured emulator executable no longer exists on disk.</summary>
    public bool IsEmulatorMissing => IsConfigured && !System.IO.File.Exists(ExecutablePath);

    /// <summary>
    /// Whether the fullscreen flag is present in the launch arguments. Toggling it edits
    /// the argument template in place - custom arguments are preserved.
    /// </summary>
    public bool FullscreenEnabled
    {
        get
        {
            var flag = EmulatorLaunchFlags.FullscreenFlagFor(ExecutablePath);
            return flag is not null && EmulatorLaunchFlags.HasFlag(Arguments, flag);
        }
        set
        {
            var flag = EmulatorLaunchFlags.FullscreenFlagFor(ExecutablePath);
            if (flag is null || value == FullscreenEnabled)
            {
                return;
            }
            var updated = value
                ? string.IsNullOrWhiteSpace(Arguments)
                    ? flag
                    : flag + " " + Arguments.Trim()
                : EmulatorLaunchFlags.WithoutFlag(Arguments, flag);
            _suppressPersist = true;
            Arguments = updated;
            _suppressPersist = false;
            OnPropertyChanged(nameof(Arguments));
            OnPropertyChanged(nameof(FullscreenEnabled));
            _database.SaveEmulatorConfig(Key, ExecutablePath, Arguments);
        }
    }

    [RelayCommand]
    private void RequestControls()
    {
        if (!IsConfigured)
        {
            _notifyStatus?.Invoke($"Configure the {DisplayName} emulator first - PokeLauncher edits that emulator's own configuration file.");
            return;
        }
        ControlsRequested?.Invoke(this);
    }

    /// <summary>Opens a file dialog; called from Settings with the window's storage provider.</summary>
    public async Task BrowseExecutableAsync(IStorageProvider? storageProvider)
    {
        if (storageProvider is null)
        {
            return;
        }
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose the {DisplayName} emulator executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } },
            },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            ExecutablePath = path;
        }
    }
}

public sealed partial class SettingsViewModel : PageViewModelBase
{
    private readonly AppDatabase _database;
    private readonly Action<AccentOption> _applyAccent;
    private readonly Action<bool> _onDefaultViewChanged;
    private readonly EmulatorDownloadService _downloadService;
    private bool _suppressAccentSideEffects = true;
    private readonly Action<int> _onBackupRetentionChanged;
    private readonly Action _onCoverArtModeChanged;
    private readonly Action<string> _onStartPageChanged;
    private readonly Action<bool> _onNotifyInboxChanged;
    private readonly Action<string>? _onThemeChanged;
    private readonly Action<string>? _onCoverSizeChanged;
    private readonly Action<double>? _onUiScaleChanged;

    public SettingsViewModel(
        AppDatabase database,
        EmulatorDownloadService downloadService,
        Action<AccentOption> applyAccent,
        Action<bool> onDefaultViewChanged,
        Action<int> onBackupRetentionChanged,
        Action onCoverArtModeChanged,
        Action<string> onStartPageChanged,
        Action<bool> onNotifyInboxChanged,
        Action<string>? onThemeChanged = null,
        Action<string>? onCoverSizeChanged = null,
        Action<double>? onUiScaleChanged = null)
    {
        _database = database;
        _downloadService = downloadService;
        _applyAccent = applyAccent;
        _onDefaultViewChanged = onDefaultViewChanged;
        _onBackupRetentionChanged = onBackupRetentionChanged;
        _onCoverArtModeChanged = onCoverArtModeChanged;
        _onStartPageChanged = onStartPageChanged;
        _onNotifyInboxChanged = onNotifyInboxChanged;
        _onThemeChanged = onThemeChanged;
        _onCoverSizeChanged = onCoverSizeChanged;
        _onUiScaleChanged = onUiScaleChanged;

        AccentOptions = new List<AccentOption>
        {
            new("Purple", "#8B5CF6", "#38BDF8"),
            new("Blue", "#38BDF8", "#2563EB"),
            new("Teal", "#2DD4BF", "#0F766E"),
            new("Magenta", "#F472B6", "#BE185D"),
        };

        var savedAccentName = database.GetSetting("ui.accent");
        var selected = AccentOptions.FirstOrDefault(a => a.Name == savedAccentName) ?? AccentOptions[0];
        foreach (var option in AccentOptions)
        {
            option.IsSelected = ReferenceEquals(option, selected);
        }
        // Restore the saved accent immediately; persistence writers stay suppressed until setup ends.
        _applyAccent(selected);
        _selectedAccent = selected;
        _suppressAccentSideEffects = false;

        _defaultLibraryGridView = database.GetSetting("library.gridView") != "list";

        _romsFolderPath = ManagedLibrary.RomsRoot;
        _savesFolderPath = ManagedLibrary.SavesRoot;

        _selectedCoverArt = database.GetSetting("ui.coverArt") == "placeholders"
            ? CoverArtOptions[1]
            : CoverArtOptions[0];
        _selectedBackupRetention = BackupRetentionOptions.FirstOrDefault(o => o.Value == ParseRetention(database.GetSetting("saves.retention")))
                                   ?? BackupRetentionOptions[2];

        var savedStartPage = database.GetSetting("ui.startPage");
        _selectedStartPage = StartPageOptions.Contains(savedStartPage, StringComparer.Ordinal)
            ? savedStartPage!
            : StartPageOptions[0];
        _notifyInboxAtStartup = database.GetSetting("startup.notifyInbox") != "0";
        _rememberWindowBounds = database.GetSetting("window.rememberBounds") != "0";

        var savedTheme = database.GetSetting("appearance.theme");
        _selectedTheme = ThemeService.ThemeOptions.Contains(savedTheme, StringComparer.OrdinalIgnoreCase)
            ? savedTheme!
            : ThemeService.DefaultTheme;
        var savedCoverSize = database.GetSetting("appearance.coverSize");
        _selectedCoverSize = ThemeService.CoverSizeOptions.Contains(savedCoverSize, StringComparer.OrdinalIgnoreCase)
            ? savedCoverSize!
            : ThemeService.DefaultCoverSize;
        var savedUiScale = database.GetSetting("appearance.uiScale");
        _selectedUiScale = ThemeService.UiScaleOptions.Contains(savedUiScale, StringComparer.OrdinalIgnoreCase)
            ? savedUiScale!
            : ThemeService.DefaultUiScale;

        var configs = database.LoadEmulatorConfigs();
        EmulatorSystemOptions = EmulatorSystems.All
            .Select(system =>
            {
                var viewModel = new EmulatorSystemViewModel(
                    database,
                    downloadService,
                    system,
                    configs.TryGetValue(system.Key, out var config)
                        ? config
                        : new EmulatorConfig { SystemKey = system.Key },
                    notifyStatus: message => StatusMessage = message);
                viewModel.InstallChanged += OnEmulatorInstallChanged;
                viewModel.ControlsRequested += systemViewModel => ControlsRequested?.Invoke(systemViewModel);
                return viewModel;
            })
            .ToList();
    }

    /// <summary>After one row installs/removes an emulator, sibling rows for the same
    /// emulator (GB/GBC/GBA → mGBA) refresh from the database so everything stays honest.</summary>
    private void OnEmulatorInstallChanged()
    {
        foreach (var system in EmulatorSystemOptions)
        {
            system.RefreshInstallState();
        }
    }

    public override string DisplayName => "Settings";

    public override string IconGlyph => "\uE713";

    public override string? HeaderSubtitle => "Preferences are saved automatically on this computer";

    /// <summary>Wired from the window once it is open; used by the folder browser.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    /// <summary>Raised when a user asks to edit an emulator system keyboard controls
    /// (MainViewModel opens the controls dialog and writes the emulator config).</summary>
    public event Action<EmulatorSystemViewModel>? ControlsRequested;

    /// <summary>Opens a folder in the system file manager; wired from the window.</summary>
    public Func<string, Task>? OpenFolderHandler { get; set; }

    public string RomsFolderPath => _romsFolderPath;

    public string SavesFolderPath => _savesFolderPath;
    private readonly string _romsFolderPath;
    private readonly string _savesFolderPath;

    public IReadOnlyList<AccentOption> AccentOptions { get; }

    public IReadOnlyList<EmulatorSystemViewModel> EmulatorSystemOptions { get; }

    public string DataLocation => _database.DatabasePath;

    public IReadOnlyList<string> DownloadServiceWarnings => BuildDownloadServiceWarnings();

    public bool HasDownloadWarnings => DownloadServiceWarnings.Count > 0;

    public string DownloadWarningsText => string.Join(" ", DownloadServiceWarnings);

    private IReadOnlyList<string> BuildDownloadServiceWarnings()
    {
        var warnings = new List<string>();
        if (!_downloadService.ManifestLoaded)
        {
            warnings.Add("The built-in emulator manifest could not be loaded, so official-build downloads are unavailable. " +
                         string.Join(" ", _downloadService.ManifestErrors.Take(2)));
        }
        else if (_downloadService.ManifestErrors.Count > 0)
        {
            warnings.Add("Some emulator manifest entries were ignored: " +
                         string.Join(" ", _downloadService.ManifestErrors.Take(2)));
        }
        if (_downloadService.ManifestStartupError is { } startupError)
        {
            warnings.Add(startupError);
        }
        return warnings;
    }

    public string PersistenceNote =>
        _database.LoadError is { } error
            ? "The local database could not be opened, so changes will not survive a restart. Error: " + error
            : "Settings and library data are saved automatically and safely restored the next time PokeLauncher starts.";

    public bool HasLoadError => _database.LoadError is not null;

    [ObservableProperty]
    private AccentOption? _selectedAccent;

    [ObservableProperty]
    private bool _defaultLibraryGridView;

    [ObservableProperty]
    private string _selectedCoverArt;

    [ObservableProperty]
    public partial string? CoverArtNote { get; private set; }

    public IReadOnlyList<string> CoverArtOptions { get; } = new[]
    {
        "Custom cover art (when available)",
        "Polished placeholders (default)",
    };

    public IReadOnlyList<BackupRetentionOption> BackupRetentionOptions { get; } = new[]
    {
        new BackupRetentionOption(5),
        new BackupRetentionOption(10),
        new BackupRetentionOption(20),
        new BackupRetentionOption(50),
    };

    [ObservableProperty]
    private BackupRetentionOption? _selectedBackupRetention;

    /// <summary>Page the app opens on (persisted; applied by MainViewModel).</summary>
    public IReadOnlyList<string> StartPageOptions { get; } = new[]
    {
        "Home", "My Library", "Recently Played",
    };

    [ObservableProperty]
    private string _selectedStartPage;

    partial void OnSelectedStartPageChanged(string value)
    {
        if (value is null || _suppressAccentSideEffects)
        {
            return;
        }
        _onStartPageChanged(value);
        SaveSetting("ui.startPage", value, $"PokeLauncher will open on {value}.");
    }

    /// <summary>Remember the main window size and position between sessions.</summary>
    [ObservableProperty]
    private bool _rememberWindowBounds;

    partial void OnRememberWindowBoundsChanged(bool value)
    {
        if (_suppressAccentSideEffects)
        {
            return;
        }
        SaveSetting("window.rememberBounds", value ? "1" : "0", "Window size and position preference updated. Saved.");
    }

    /// <summary>Show a toast at startup when ROM files are waiting in the inbox.</summary>
    [ObservableProperty]
    private bool _notifyInboxAtStartup;

    partial void OnNotifyInboxAtStartupChanged(bool value)
    {
        if (_suppressAccentSideEffects)
        {
            return;
        }
        _onNotifyInboxChanged(value);
        SaveSetting("startup.notifyInbox", value ? "1" : "0", "Inbox notification preference updated. Saved.");
    }

    partial void OnSelectedBackupRetentionChanged(BackupRetentionOption? value)
    {
        if (value is null)
        {
            return;
        }
        _onBackupRetentionChanged(value.Value);
        StatusMessage = SaveSetting("saves.retention", value.Value.ToString(), $"Save backups per game set to {value.Value}.");
    }

    public bool HasStatusMessage => StatusMessage is not null;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>App theme choices for the appearance section.</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = ThemeService.ThemeOptions;

    /// <summary>Game-card cover sizes for grid view.</summary>
    public IReadOnlyList<string> CoverSizeOptions { get; } = ThemeService.CoverSizeOptions;

    /// <summary>Interface scale choices.</summary>
    public IReadOnlyList<string> UiScaleOptions { get; } = ThemeService.UiScaleOptions;

    [ObservableProperty]
    private string _selectedTheme = ThemeService.DefaultTheme;

    [ObservableProperty]
    private string _selectedCoverSize = ThemeService.DefaultCoverSize;

    [ObservableProperty]
    private string _selectedUiScale = ThemeService.DefaultUiScale;

    partial void OnSelectedThemeChanged(string value)
    {
        _onThemeChanged?.Invoke(value);
        StatusMessage = SaveSetting("appearance.theme", value, $"Theme changed to {value}. Saved.");
    }

    partial void OnSelectedCoverSizeChanged(string value)
    {
        _onCoverSizeChanged?.Invoke(value);
        StatusMessage = SaveSetting("appearance.coverSize", value, $"{value} covers. Saved.");
    }

    partial void OnSelectedUiScaleChanged(string value)
    {
        if (double.TryParse(value.TrimEnd('%'), out var percent))
        {
            _onUiScaleChanged?.Invoke(percent / 100.0);
        }
        StatusMessage = SaveSetting("appearance.uiScale", value, $"Interface scaled to {value}. Saved.");
    }

    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        if (value is null || _suppressAccentSideEffects)
        {
            return;
        }
        foreach (var option in AccentOptions)
        {
            option.IsSelected = ReferenceEquals(option, value);
        }
        _applyAccent(value);
        _database.SetSetting("ui.accent", value.Name);
        StatusMessage = $"Accent changed to {value.Name}. Saved.";
    }

    partial void OnDefaultLibraryGridViewChanged(bool value)
    {
        _onDefaultViewChanged(value);
        StatusMessage = SaveSetting("library.gridView", value ? "grid" : "list", "Default library view updated. Saved.");
    }

    [RelayCommand]
    private async Task OpenFolderAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        if (OpenFolderHandler is null)
        {
            StatusMessage = "The folder can't be opened yet — wait for the window to finish loading.";
            return;
        }
        await OpenFolderHandler(path);
    }

    private static int ParseRetention(string? value) =>
        int.TryParse(value, out var parsed) && parsed is >= 1 and <= 100
            ? parsed
            : SaveBackupService.DefaultBackupsPerGame;

    private string? SaveSetting(string key, string value, string message)
    {
        _database.SetSetting(key, value);
        return _database.LoadError is null ? message : null;
    }

    partial void OnSelectedCoverArtChanged(string value)
    {
        var showCustom = value.StartsWith("Custom", StringComparison.Ordinal);
        CoverArtNote = showCustom
            ? "Games with a cover image show it — set one from any game's page (Set cover art)."
            : "Generated placeholder art is shown everywhere; cover images are kept on disk.";
        _onCoverArtModeChanged();
        SaveSetting("ui.coverArt", showCustom ? "custom" : "placeholders", "Cover art preference updated. Saved.");
    }

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    [RelayCommand]
    private void ClearStatus() => StatusMessage = null;

    [RelayCommand]
    private void ApplyAccent(AccentOption option) => SelectedAccent = option;

    [RelayCommand]
    private Task BrowseEmulatorExecutableAsync(EmulatorSystemViewModel? system) =>
        system is null ? Task.CompletedTask : system.BrowseExecutableAsync(StorageProvider);
}
