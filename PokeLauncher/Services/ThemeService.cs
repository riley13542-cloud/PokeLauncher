using System;
using Avalonia;
using Avalonia.Media;

namespace PokeLauncher.Services;

/// <summary>
/// Applies the appearance preferences to the live Avalonia resource layer, the same
/// way the accent color works: theme surface/text tokens and card-cover dimensions
/// are plain resource keys that every view reads through DynamicResource, so swapping
/// them restyles the whole app instantly without restarting.
/// Accent brushes are owned by the accent picker and are never touched here.
/// </summary>
public static class ThemeService
{
    public static readonly string[] ThemeOptions = { "Dark", "Light", "Midnight (OLED)" };
    public static readonly string[] CoverSizeOptions = { "Small", "Medium", "Large" };
    public static readonly string[] UiScaleOptions = { "90%", "100%", "115%", "130%" };

    public const string DefaultTheme = "Dark";
    public const string DefaultCoverSize = "Medium";
    public const string DefaultUiScale = "100%";

    /// <summary>Applies a named theme ("Dark", "Light", "Midnight (OLED)").</summary>
    public static void ApplyTheme(string? themeName)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var isLight = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
        var isMidnight = string.Equals(themeName, "Midnight (OLED)", StringComparison.OrdinalIgnoreCase);

        // Built-in controls (ComboBox, TextBox, CheckBox) follow the Fluent theme
        // variant; the app's own brushes below cover everything else.
        app.RequestedThemeVariant = isLight
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;

        if (isLight)
        {
            Set(app, "BgBaseBrush", "#F2F2F7");
            Set(app, "SidebarBrush", "#E8E8F0");
            Set(app, "TopBarBrush", "#FFFFFF");
            Set(app, "SurfaceBrush", "#FFFFFF");
            Set(app, "SurfaceAltBrush", "#ECECF3");
            Set(app, "SubtleBorderBrush", "#24000000");
            Set(app, "TextPrimaryBrush", "#1C1C24");
            Set(app, "TextSecondaryBrush", "#993C3C46");
            Set(app, "TextTertiaryBrush", "#593C3C46");
            Set(app, "InputBackgroundBrush", "#FFFFFF");
            Set(app, "SurfaceHoverBrush", "#E4E4EC");
        }
        else if (isMidnight)
        {
            Set(app, "BgBaseBrush", "#000000");
            Set(app, "SidebarBrush", "#050508");
            Set(app, "TopBarBrush", "#0A0A0E");
            Set(app, "SurfaceBrush", "#0E0E13");
            Set(app, "SurfaceAltBrush", "#181820");
            Set(app, "SubtleBorderBrush", "#26FFFFFF");
            Set(app, "TextPrimaryBrush", "#F2F2F7");
            Set(app, "TextSecondaryBrush", "#99EBEBF5");
            Set(app, "TextTertiaryBrush", "#66EBEBF5");
            Set(app, "InputBackgroundBrush", "#1A1A23");
            Set(app, "SurfaceHoverBrush", "#22222C");
        }
        else
        {
            // Dark: the Tokens.axaml defaults, restated so switching back from
            // Light/Midnight restores them exactly.
            Set(app, "BgBaseBrush", "#14141B");
            Set(app, "SidebarBrush", "#0F0F16");
            Set(app, "TopBarBrush", "#171720");
            Set(app, "SurfaceBrush", "#1C1C26");
            Set(app, "SurfaceAltBrush", "#252530");
            Set(app, "SubtleBorderBrush", "#22FFFFFF");
            Set(app, "TextPrimaryBrush", "#F2F2F7");
            Set(app, "TextSecondaryBrush", "#99EBEBF5");
            Set(app, "TextTertiaryBrush", "#66EBEBF5");
            Set(app, "InputBackgroundBrush", "#2A2A38");
            Set(app, "SurfaceHoverBrush", "#2E2E3C");
        }
    }

    /// <summary>Applies the game-card cover size ("Small", "Medium", "Large").</summary>
    public static void ApplyCoverSize(string? sizeName)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        // Aspect matches the 176x208 card cover.
        var (width, height) = sizeName switch
        {
            "Small" => (144.0, 170.0),
            "Large" => (208.0, 246.0),
            _ => (176.0, 208.0),
        };
        app.Resources["CardCoverWidth"] = width;
        app.Resources["CardCoverHeight"] = height;
    }

    private static void Set(Application app, string key, string hex)
    {
        app.Resources[key] = new SolidColorBrush(Color.Parse(hex));
    }
}
