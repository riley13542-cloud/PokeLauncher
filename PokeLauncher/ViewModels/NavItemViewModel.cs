using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PokeLauncher.ViewModels;

/// <summary>
/// Sidebar entry wrapping a page. Checking the associated radio button requests navigation.
/// </summary>
public partial class NavItemViewModel : ViewModelBase
{
    private readonly Action<PageViewModelBase> _navigate;

    public NavItemViewModel(PageViewModelBase page, Action<PageViewModelBase> navigate)
    {
        Page = page;
        _navigate = navigate;
    }

    public PageViewModelBase Page { get; }

    public string DisplayName => Page.DisplayName;

    public string IconGlyph => Page.IconGlyph;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    partial void OnIsCurrentChanged(bool value)
    {
        if (value)
        {
            _navigate(Page);
        }
    }
}
