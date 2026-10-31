using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PokeLauncher.Controls;

/// <summary>
/// Placeholder cover art used everywhere until real cover images are supported.
/// </summary>
public partial class CoverPlaceholder : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CoverPlaceholder, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<CoverPlaceholder, string?>(nameof(Subtitle));

    public static readonly StyledProperty<Color> FromColorProperty =
        AvaloniaProperty.Register<CoverPlaceholder, Color>(nameof(FromColor), Color.Parse("#2E2E3C"));

    public static readonly StyledProperty<Color> ToColorProperty =
        AvaloniaProperty.Register<CoverPlaceholder, Color>(nameof(ToColor), Color.Parse("#1C1C26"));

    public static readonly StyledProperty<bool> ShowTextProperty =
        AvaloniaProperty.Register<CoverPlaceholder, bool>(nameof(ShowText), true);

    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<CoverPlaceholder, double>(nameof(TitleFontSize), 15d);

    public static readonly StyledProperty<double> BallSizeProperty =
        AvaloniaProperty.Register<CoverPlaceholder, double>(nameof(BallSize), 56d);

    public static readonly StyledProperty<double> BallOpacityProperty =
        AvaloniaProperty.Register<CoverPlaceholder, double>(nameof(BallOpacity), 0.55d);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Color FromColor
    {
        get => GetValue(FromColorProperty);
        set => SetValue(FromColorProperty, value);
    }

    public Color ToColor
    {
        get => GetValue(ToColorProperty);
        set => SetValue(ToColorProperty, value);
    }

    public bool ShowText
    {
        get => GetValue(ShowTextProperty);
        set => SetValue(ShowTextProperty, value);
    }

    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public double BallSize
    {
        get => GetValue(BallSizeProperty);
        set => SetValue(BallSizeProperty, value);
    }

    public double BallOpacity
    {
        get => GetValue(BallOpacityProperty);
        set => SetValue(BallOpacityProperty, value);
    }

    public CoverPlaceholder()
    {
        InitializeComponent();
    }
}
