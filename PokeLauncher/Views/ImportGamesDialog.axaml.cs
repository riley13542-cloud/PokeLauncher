using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

/// <summary>
/// Modal review step for the ROM import flow. The view model decides the outcome
/// (confirm/cancel); this window only hosts it and closes with the requested result.
/// </summary>
public partial class ImportGamesDialog : Window
{
    public ImportGamesDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is ImportGamesDialogViewModel viewModel)
        {
            viewModel.RequestClose += OnRequestClose;
        }
        // Put keyboard focus on a text field, never on the confirm button, so a stray
        // Enter cannot import files the user hasn't reviewed.
        this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    private void OnRequestClose(bool confirmed) => Close(confirmed);
}
