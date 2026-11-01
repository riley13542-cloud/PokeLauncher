using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Views;

/// <summary>
/// Keyboard-controls editor for an emulator system. The window captures key presses
/// while a row is armed; the view model holds the mapping and the host saves it.
/// </summary>
public partial class ControlsDialog : Window
{
    private ControlsDialogViewModel? _viewModel;

    public ControlsDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _viewModel = DataContext as ControlsDialogViewModel;
        if (_viewModel is not null)
        {
            // Save closes the dialog with the final map (MainViewModel writes it to
            // the emulator's config); Cancel closes with null (no changes saved).
            // The result MUST match the ShowDialog<T> type parameter in
            // MainViewModel - a bool here throws InvalidCastException on resume
            // and crashes the app.
            _viewModel.SaveRequested += map => Close(map);
            _viewModel.RequestClose += _ => Close(null);
        }
        this.GetVisualDescendants().OfType<Control>().FirstOrDefault()?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.ApplyCapturedKey(e.Key) == true)
        {
            e.Handled = true;
        }
    }
}
