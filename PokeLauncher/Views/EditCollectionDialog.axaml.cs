using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

/// <summary>
/// Modal create/edit dialog for a collection (name, description, members, delete).
/// The view model decides the outcome; this window only hosts it.
/// </summary>
public partial class EditCollectionDialog : Window
{
    public EditCollectionDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is EditCollectionDialogViewModel viewModel)
        {
            viewModel.RequestClose += OnRequestClose;
            viewModel.DeleteConfirmed += () => Close("delete");
        }
        // Focus the name field rather than a destructive button.
        this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    private void OnRequestClose(bool saved) => Close(saved);
}
