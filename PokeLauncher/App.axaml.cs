using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PokeLauncher.Services;
using PokeLauncher.ViewModels;
using PokeLauncher.Views;

namespace PokeLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Local persistence: one SQLite database under %LocalAppData%\PokeLauncher.
            var database = new AppDatabase();
            database.Initialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(database),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}