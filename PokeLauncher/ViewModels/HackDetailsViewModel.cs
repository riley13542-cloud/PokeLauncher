using System;
using System.Threading.Tasks;

using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PokeLauncher.ViewModels;

/// <summary>
/// Details page for a user-created ROM hack: launch (through the base game's emulator),
/// edit, delete, bookmark, and open the hack's official page.
/// </summary>
public sealed partial class RomHackDetailsViewModel : PageViewModelBase
{
    private readonly Action<string> _showToast;
    private readonly Func<RomHackViewModel, Task> _playHack;
    private readonly Func<RomHackViewModel, Task> _editHack;
    private readonly Action _openSettings;

    public RomHackDetailsViewModel(
        Action<string> showToast,
        Func<RomHackViewModel, Task> playHack,
        Func<RomHackViewModel, Task> editHack,
        Action openSettings)
    {
        _showToast = showToast;
        _playHack = playHack;
        _editHack = editHack;
        _openSettings = openSettings;
    }

    public override string DisplayName => "ROM Hack Details";

    public override string IconGlyph => "\uE774";

    [ObservableProperty]
    public partial RomHackViewModel? Hack { get; private set; }

    public bool HasHack => Hack is not null;

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

    public bool HasLaunchError => LaunchError is not null;

    [ObservableProperty]
    public partial string? LaunchError { get; private set; }

    [ObservableProperty]
    public partial bool LaunchErrorNeedsEmulator { get; private set; }

    /// <summary>Wired from the window once it is open; used only to open external pages.</summary>
    public ILauncher? Launcher { get; set; }

    public void SetHack(RomHackViewModel? hack)
    {
        Hack = hack;
        LaunchError = null;
        LaunchErrorNeedsEmulator = false;
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

    partial void OnHackChanged(RomHackViewModel? value) => OnPropertyChanged(nameof(HasHack));

    [RelayCommand]
    private Task Play() => Hack is { } hack ? _playHack(hack) : Task.CompletedTask;

    [RelayCommand]
    private Task EditDetails() => Hack is { } hack ? _editHack(hack) : Task.CompletedTask;

    [RelayCommand]
    private void OpenEmulatorSettings() => _openSettings();

    [RelayCommand]
    private async Task OpenExternalPageAsync()
    {
        var url = Hack?.ExternalUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        if (Launcher is null)
        {
            _showToast("External links become available once the window finishes loading — try again.");
            return;
        }
        var launched = await Launcher.LaunchUriAsync(new Uri(url));
        if (!launched)
        {
            _showToast("Couldn't open the external page — no browser handler was available.");
        }
    }
}
