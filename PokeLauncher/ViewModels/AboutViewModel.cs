using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

public sealed class AboutStatusItem
{
    public AboutStatusItem(string text, bool isDone)
    {
        Text = text;
        IsDone = isDone;
    }

    public string Text { get; }

    public bool IsDone { get; }
}

public sealed partial class AboutViewModel : PageViewModelBase
{
    private readonly UpdateService _updates = new();

    public AboutViewModel()
    {
        BrandLogo = ManagedLibrary.LoadLogo();
    }

    public Avalonia.Media.IImage? BrandLogo { get; }

    public bool UpdatesConfigured => _updates.IsConfigured;

    [ObservableProperty]
    public partial string? UpdateStatus { get; private set; }

    [ObservableProperty]
    public partial bool IsCheckingForUpdate { get; private set; }

    [ObservableProperty]
    public partial string? PendingUpdateMessage { get; private set; }

    public string? PendingInstallerPath { get; private set; }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsCheckingForUpdate)
        {
            return;
        }
        IsCheckingForUpdate = true;
        UpdateStatus = "Checking…";
        try
        {
            var result = await _updates.CheckForUpdatesAsync(Version);
            UpdateStatus = result.Message;
            PendingUpdateMessage = result.Success && result.UpdateAvailable
                ? $"{result.Message} Download it, then close PokeLauncher and run the installer."
                : null;
            _pendingManifest = result.Manifest;
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }
    private UpdateManifest? _pendingManifest;

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (_pendingManifest is null || IsDownloadingUpdate)
        {
            return;
        }
        IsDownloadingUpdate = true;
        UpdateStatus = "Downloading…";
        try
        {
            var progress = new Progress<double>(percent => UpdateStatus = $"Downloading… {percent:0}%");
            var path = await _updates.DownloadInstallerAsync(_pendingManifest, progress);
            PendingInstallerPath = path;
            PendingUpdateMessage = $"Version {_pendingManifest.Version} downloaded and verified. Close PokeLauncher and run the installer to finish updating.";
            UpdateStatus = $"Downloaded {System.IO.Path.GetFileName(path)} — close PokeLauncher, then run it.";

        }
        catch (Exception exception)
        {
            UpdateStatus = $"The update couldn't be downloaded: {exception.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    public bool IsDownloadingUpdate { get; private set; }
    public override string DisplayName => "About";

    public override string IconGlyph => "\uE946";

    public override string? HeaderSubtitle => "About this application";

    public string AppName => "PokeLauncher";

    public string Version => "0.11.1-distribution";

    public string Tagline => "Organize, discover, and launch your Pokémon collection.";

    public string Description =>
        "PokeLauncher is a desktop launcher for official Generations I–IV games and community ROM hacks. " +
        "This release pass adds your own logo support, built-in cover art for every Gen I–IV game, a ROM inbox " +
        "that attaches dropped files to the built-in catalog, a fully working ROM hack library (add your own " +
        "hacks and launch them), an installer, and a built-in update checker for future releases.";

    public string Disclaimer =>
        "PokeLauncher never includes or distributes ROM files — the built-in Gen I–IV catalog entries are " +
        "organizing slots for games you already own. Drop your own legally obtained ROM files into the " +
        "managed ROM inbox (or import them) and they attach to those entries. Emulator software is never " +
        "bundled; it is downloaded only when you ask, directly from each project's official site.";

    /// <summary>Highlights of the current release, shown on the About page.</summary>
    public IReadOnlyList<string> WhatsNew { get; } = new[]
    {
        "Brand logo: drop your own logo.png into the PokeLauncher data folder to replace the emblem everywhere",
        "Cover art: built-in covers for all 16 Gen I–IV games, plus set-your-own on any game's page",
        "ROM inbox: drop Gen I–IV ROM files into ROMs\\Inbox and attach them to the built-in catalog in one confirmed step",
        "ROM hack library: add your own hacks, attach the patched ROM, and launch them",
        "Library: new Missing ROMs filter, import dates on game pages, and collection toggles on game details",
        "Save backups: choose how many snapshots to keep per game",
        "Updates: built-in update checker + installer for future releases",
    };

    public IReadOnlyList<string> TechStack { get; } = new[]
    {
        "C# / .NET 10",
        "Avalonia UI 12",
        "CommunityToolkit.Mvvm",
        "Microsoft.Data.Sqlite",
    };

    public IReadOnlyList<AboutStatusItem> Statuses { get; } = new[]
    {
        new AboutStatusItem("Sidebar navigation between pages", true),
        new AboutStatusItem("Home dashboard with hero and sections", true),
        new AboutStatusItem("Library with search, filters, sorting, and view modes", true),
        new AboutStatusItem("ROM Hack Browser with featured banner and filters", true),
        new AboutStatusItem("Details pages for games and ROM hacks", true),
        new AboutStatusItem("Favorites, Recently Played, and Collections", true),
        new AboutStatusItem("Persistent local database (SQLite) for library state", true),
        new AboutStatusItem("Saved favorites, bookmarks, and settings", true),
        new AboutStatusItem("Per-system emulator configuration in Settings", true),
        new AboutStatusItem("Consent-driven official emulator downloads with SHA-256 verification", true),
        new AboutStatusItem("ROM import with system detection, review dialog, and duplicate protection", true),
        new AboutStatusItem("Game metadata editing (title, platform, generation, description)", true),
        new AboutStatusItem("Game launching through configured emulators with {rom} arguments", true),
        new AboutStatusItem("Automatic playtime tracking with per-session history in SQLite", true),
        new AboutStatusItem("Custom collections: create, rename, re-member, and delete", true),
        new AboutStatusItem("Recent sessions and collection membership on game details", true),
        new AboutStatusItem("Managed library: imported ROMs copied into PokeLauncher's data folder", true),
        new AboutStatusItem("Automatic save backups before every launch, with restore", true),
        new AboutStatusItem("Custom brand logo and per-game cover art", true),
        new AboutStatusItem("ROM inbox for attaching your own Gen I–IV files to the built-in catalog", true),
        new AboutStatusItem("ROM hack library: add, edit, and launch your own hacks", true),
        new AboutStatusItem("Built-in update checker with SHA-256-verified downloads", true),
        new AboutStatusItem("Bundled Gen I–IV ROM files", false),
        new AboutStatusItem("Save-state cloud sync and patching", false),
    };
}
