namespace PokeLauncher.ViewModels;

/// <summary>
/// A page whose content can be filtered by the global search field in the top bar.
/// </summary>
public interface ISearchablePage
{
    string? SearchQuery { get; set; }
}

/// <summary>
/// A page that can re-apply its filters/sorting when the top-bar refresh action is used.
/// </summary>
public interface IRefreshablePage
{
    void Refresh();
}
