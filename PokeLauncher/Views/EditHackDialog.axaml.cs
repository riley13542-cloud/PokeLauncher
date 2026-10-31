using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

/// <summary>
/// Modal create/edit dialog for a user-created ROM hack. The view model decides the
/// outcome; the host (MainViewModel) handles the ROM file picker through BrowseRomRequested.
/// </summary>
public partial class EditHackDialog : Window
{
    public EditHackDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is EditHackDialogViewModel viewModel)
        {
            viewModel.RequestClose += OnRequestClose;
            viewModel.DeleteConfirmed += () => Close("delete");
        }
        this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    private void OnRequestClose(bool saved) => Close(saved);
}
