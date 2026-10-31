using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

/// <summary>
/// Modal editor for a game's metadata (and, for imported games, removal / ROM detach).
/// The view model decides the outcome; this window only hosts it.
/// </summary>
public partial class EditGameDialog : Window
{
    public EditGameDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is EditGameDialogViewModel viewModel)
        {
            viewModel.RequestClose += OnRequestClose;
            viewModel.RemoveConfirmed += () => Close("remove");
            viewModel.DetachRomConfirmed += () => Close("detach");
        }
        // Focus the title field rather than a destructive button.
        this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    private void OnRequestClose(bool saved) => Close(saved);
}
