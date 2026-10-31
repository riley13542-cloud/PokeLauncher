using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PokeLauncher.Services;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
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
        }
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
            if (ManagedLibrary.LogoFileExists())
            {
                if (ManagedLibrary.LoadLogo() is { } bitmap)
                {
                    mainViewModel.BrandLogo = bitmap;
                    Icon = new WindowIcon(bitmap);
                }
            }
        }
        catch
        {
            /* a broken logo.png must not break startup; the bundled icon stays */
        }
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
