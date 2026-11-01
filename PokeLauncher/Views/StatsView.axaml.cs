using System;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace PokeLauncher.Views;

public partial class StatsView : UserControl
{
    /// <summary>Scales a 0..1 playtime share into pixels for the most-played bars.</summary>
    public static readonly FuncValueConverter<double, double> BarWidthConverter =
        new(percent => Math.Clamp(percent, 0.02, 1.0) * 420.0);

    public StatsView()
    {
        InitializeComponent();
    }
}
