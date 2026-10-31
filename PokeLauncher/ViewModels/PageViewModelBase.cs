namespace PokeLauncher.ViewModels;

/// <summary>
/// Base class for view models that represent a page of the main window.
/// </summary>
public abstract class PageViewModelBase : ViewModelBase
{
    /// <summary>Name shown in the sidebar navigation and the top bar.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Segoe Fluent Icons glyph shown next to the sidebar label.</summary>
    public abstract string IconGlyph { get; }

    /// <summary>Optional subtitle shown under the page title in the top bar.</summary>
    public virtual string? HeaderSubtitle => null;
}
