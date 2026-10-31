using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PokeLauncher.ViewModels;

namespace PokeLauncher.Controls;

/// <summary>
/// A game's cover: shows the custom cover image (Covers\<game-id>.png/.jpg/…) when one is
/// set and custom covers are enabled in Settings; otherwise the generated placeholder art.
/// Passes the same visual properties as CoverPlaceholder so existing call sites only swap
/// the element name.
/// </summary>
public partial class GameCover : UserControl
{
    public static readonly StyledProperty<GameViewModel?> GameProperty =
        AvaloniaProperty.Register<GameCover, GameViewModel?>(nameof(Game));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<GameCover, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<GameCover, string?>(nameof(Subtitle));

    public static readonly StyledProperty<Color> FromColorProperty =
        AvaloniaProperty.Register<GameCover, Color>(nameof(FromColor), Color.Parse("#2E2E3C"));

    public static readonly StyledProperty<Color> ToColorProperty =
        AvaloniaProperty.Register<GameCover, Color>(nameof(ToColor), Color.Parse("#1C1C26"));

    public static readonly StyledProperty<bool> ShowTextProperty =
        AvaloniaProperty.Register<GameCover, bool>(nameof(ShowText), true);

    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<GameCover, double>(nameof(TitleFontSize), 15d);

    public static readonly StyledProperty<double> BallSizeProperty =
        AvaloniaProperty.Register<GameCover, double>(nameof(BallSize), 56d);

    public static readonly StyledProperty<double> BallOpacityProperty =
        AvaloniaProperty.Register<GameCover, double>(nameof(BallOpacity), 0.55d);

    public static readonly StyledProperty<bool> ShowPlaceholderProperty =
        AvaloniaProperty.Register<GameCover, bool>(nameof(ShowPlaceholder), true);

    public static readonly StyledProperty<bool> ShowImageProperty =
        AvaloniaProperty.Register<GameCover, bool>(nameof(ShowImage));

    public GameViewModel? Game
    {
        get => GetValue(GameProperty);
        set => SetValue(GameProperty, value);
    }

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

    /// <summary>Placeholder art shows when there is no cover image to display.</summary>
    public bool ShowPlaceholder
    {
        get => GetValue(ShowPlaceholderProperty);
        private set => SetValue(ShowPlaceholderProperty, value);
    }

    /// <summary>The cover image shows when the game has one and custom covers are enabled.</summary>
    public bool ShowImage
    {
        get => GetValue(ShowImageProperty);
        private set => SetValue(ShowImageProperty, value);
    }

    private System.ComponentModel.INotifyPropertyChanged? _observedGame;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == GameProperty)
        {
            if (_observedGame is not null)
            {
                _observedGame.PropertyChanged -= OnGamePropertyChanged;
            }
            _observedGame = Game;
            if (_observedGame is not null)
            {
                _observedGame.PropertyChanged += OnGamePropertyChanged;
            }
            UpdateVisibility();
        }
    }

    private void OnGamePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.CoverImage) or nameof(GameViewModel.HasCoverArt))
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        var hasCover = Game?.HasCoverArt == true;
        ShowImage = hasCover;
        ShowPlaceholder = !hasCover;
    }

    public GameCover()
    {
        InitializeComponent();
    }
}
