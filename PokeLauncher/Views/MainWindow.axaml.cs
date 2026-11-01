using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PokeLauncher.Services;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

public partial class MainWindow : Window
{
    private const string RememberBoundsKey = "window.rememberBounds";
    private PixelPoint _lastNormalPosition;
    private Size _lastNormalSize;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Track the last non-maximized bounds so restoring a maximized window
        // doesn't persist the full-screen rectangle as the normal size.
        if (e.Property == WindowStateProperty && WindowState == WindowState.Normal)
        {
            _lastNormalPosition = Position;
            _lastNormalSize = new Size(Width, Height);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.Settings.StorageProvider = StorageProvider;
            mainViewModel.HackDetails.Launcher = Launcher;
            mainViewModel.StorageProvider = StorageProvider;
            mainViewModel.DialogOwner = this;
            mainViewModel.Settings.OpenFolderHandler = OpenFolderAsync;
            ApplyBrandLogo(mainViewModel);
            RestoreWindowBounds(mainViewModel.Database);
            ApplyUiScale(mainViewModel);
        }
    }

    // ---------------------------------------------------------- interface scale

    private void ApplyUiScale(MainViewModel mainViewModel)
    {
        mainViewModel.UiScaleChanged += scale => UiScaleHost.LayoutTransform = new ScaleTransform(scale, scale);
        UiScaleHost.LayoutTransform = new ScaleTransform(mainViewModel.CurrentUiScale, mainViewModel.CurrentUiScale);
    }

    /// <summary>
    /// The brand logo: a user-supplied %LocalAppData%\PokeLauncher\logo.png overrides the
    /// bundled app emblem - used for the sidebar mark and the window/taskbar icon.
    /// Replacing the file takes effect on the next app start.
    /// </summary>
    private void ApplyBrandLogo(MainViewModel mainViewModel)
    {
        try
        {
            var logoPath = ManagedLibrary.LogoPath;
            if (File.Exists(logoPath))
            {
                using var stream = File.OpenRead(logoPath);
                var bitmap = new Bitmap(stream);
                mainViewModel.BrandLogo = bitmap;
                Icon = new WindowIcon(bitmap);
            }
        }
        catch
        {
            /* a broken logo.png must not break startup; the bundled icon stays */
        }
    }

    // ---------------------------------------------------------- window bounds

    private void RestoreWindowBounds(AppDatabase database)
    {
        try
        {
            if (database.GetSetting(RememberBoundsKey) == "0")
            {
                return;
            }

            var left = ParseDouble(database.GetSetting("window.left"));
            var top = ParseDouble(database.GetSetting("window.top"));
            var width = ParseDouble(database.GetSetting("window.width"));
            var height = ParseDouble(database.GetSetting("window.height"));
            if (left is null || top is null || width is null || height is null)
            {
                return;
            }

            Position = new PixelPoint((int)left.Value, (int)top.Value);
            Width = width.Value;
            Height = height.Value;

            if (database.GetSetting("window.maximized") == "1")
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch
        {
            /* malformed bounds: keep the defaults */
        }
    }

    private static double? ParseDouble(string? raw)
    {
        return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel)
        {
            return;
        }

        try
        {
            if (mainViewModel.Database.GetSetting(RememberBoundsKey) == "0")
            {
                mainViewModel.Shutdown();
                return;
            }

            var state = WindowState;
            var position = WindowState == WindowState.Normal ? Position : _lastNormalPosition;
            var width = WindowState == WindowState.Normal ? Width : _lastNormalSize.Width;
            var height = WindowState == WindowState.Normal ? Height : _lastNormalSize.Height;

            mainViewModel.Database.SetSetting("window.left",
                position.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
            mainViewModel.Database.SetSetting("window.top",
                position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            mainViewModel.Database.SetSetting("window.width",
                width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            mainViewModel.Database.SetSetting("window.height",
                height.ToString(System.Globalization.CultureInfo.InvariantCulture));
            mainViewModel.Database.SetSetting("window.maximized",
                state == WindowState.Maximized ? "1" : "0");
        }
        catch
        {
            /* failing to save bounds must never block closing */
        }
        mainViewModel.Shutdown();
    }

    private static Task OpenFolderAsync(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            /* no file manager available; the path is still visible in the UI */
        }
        return Task.CompletedTask;
    }
}
