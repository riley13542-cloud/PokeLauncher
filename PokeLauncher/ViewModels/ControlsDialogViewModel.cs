using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeLauncher.Services;

namespace PokeLauncher.ViewModels;

/// <summary>One mappable console button row in the controls dialog.</summary>
public sealed partial class ControlButtonRow : ObservableObject
{
    public ControlButtonRow(EmulatorButton button, int code)
    {
        Button = button;
        _code = code;
        UpdateDisplay();
    }

    public EmulatorButton Button { get; }

    [ObservableProperty]
    private int _code;

    [ObservableProperty]
    private string _display = string.Empty;

    [ObservableProperty]
    private bool _isAssigning;

    public string ButtonLabel => Button.Label;

    public string ConfigId => Button.Id;

    partial void OnCodeChanged(int value) => UpdateDisplay();

    partial void OnIsAssigningChanged(bool value) => UpdateDisplay();

    public void UpdateDisplay()
    {
        if (IsAssigning)
        {
            Display = "Press a key…";
        }
        else if (Code < 0)
        {
            Display = "Not set";
        }
        else
        {
            Display = KeyMap.DisplayNameFor(Code) ?? $"Key {Code}";
        }
        OnPropertyChanged(nameof(Display));
    }
}

/// <summary>
/// Keyboard-controls dialog state for one emulator system: each console button maps
/// to a keyboard key (stored as Qt key codes in the emulator's own config file).
/// </summary>
public sealed partial class ControlsDialogViewModel : ObservableObject
{
    public string SystemKey { get; }

    public string SystemName { get; }

    public string EmulatorName { get; }

    public string ConfigPath { get; }

    public ObservableCollection<ControlButtonRow> Buttons { get; }

    [ObservableProperty]
    private ControlButtonRow? _armedRow;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    public bool CanSave => Buttons.Count > 0;

    /// <summary>Raised with the final map when the user confirms (dialog closes).</summary>
    public event Action<IReadOnlyDictionary<string, int>>? SaveRequested;

    public ControlsDialogViewModel(
        string systemKey,
        string systemName,
        string emulatorName,
        string configPath,
        IReadOnlyDictionary<string, int> currentMap,
        IReadOnlyList<EmulatorButton> buttons)
    {
        SystemKey = systemKey;
        SystemName = systemName;
        EmulatorName = emulatorName;
        ConfigPath = configPath;
        Buttons = new ObservableCollection<ControlButtonRow>(
            buttons.Select(b => new ControlButtonRow(b, currentMap.TryGetValue(b.Id, out var code) ? code : -1)));
    }

    public string HeaderText => $"{SystemName} keyboard controls - {EmulatorName}";

    /// <summary>Arms a row for key capture; arming another row disarms the previous one.</summary>
    [RelayCommand]
    public void ArmRow(ControlButtonRow row)
    {
        foreach (var other in Buttons.Where(r => !ReferenceEquals(r, row) && r.IsAssigning))
        {
            other.IsAssigning = false;
            other.UpdateDisplay();
        }
        row.IsAssigning = !row.IsAssigning;
        row.UpdateDisplay();
    }

    /// <summary>Applies a captured key to the armed row (called by the window on KeyDown).
    /// Esc disarms the row without assigning, matching the dialog's tip text.</summary>
    public bool ApplyCapturedKey(Avalonia.Input.Key key)
    {
        var armed = Buttons.FirstOrDefault(r => r.IsAssigning);
        if (armed is null)
        {
            return false;
        }
        if (key == Avalonia.Input.Key.Escape)
        {
            armed.IsAssigning = false;
            armed.UpdateDisplay();
            return true;
        }
        var resolved = KeyMap.Resolve(key);
        if (resolved is not { } match)
        {
            return false; // unsupported key: keep waiting
        }
        armed.Code = match.Code;
        armed.IsAssigning = false;
        armed.UpdateDisplay();
        return true;
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var row in Buttons)
        {
            row.Code = -1;
            row.IsAssigning = false;
            row.UpdateDisplay();
        }
    }

    /// <summary>Raised to close the dialog (true = save the current mapping).</summary>
    public event Action<bool>? RequestClose;

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private void Save()
    {
        SaveRequested?.Invoke(ConfirmedMap());
    }

    public IReadOnlyDictionary<string, int> ConfirmedMap()
    {
        var map = new Dictionary<string, int>();
        foreach (var row in Buttons)
        {
            map[row.ConfigId] = row.Code;
        }
        return map;
    }
}
