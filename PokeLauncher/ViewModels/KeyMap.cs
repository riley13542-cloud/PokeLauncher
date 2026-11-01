using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace PokeLauncher.ViewModels;

/// <summary>
/// Maps Avalonia key presses to the Qt key codes the emulators store in their
/// configuration files, and provides friendly display names.
/// </summary>
public static class KeyMap
{
    public static (int Code, string DisplayName)? Resolve(Key key)
    {
        var name = key.ToString();
        return name switch
        {
            "Space" => (32, "Space"),
            "Return" or "Enter" => (16777220, "Enter"),
            "Back" or "Backspace" => (16777219, "Backspace"),
            "Escape" => (16777216, "Esc"),
            "Tab" => (16777217, "Tab"),
            "Left" => (16777234, "Left arrow"),
            "Up" => (16777235, "Up arrow"),
            "Right" => (16777236, "Right arrow"),
            "Down" => (16777237, "Down arrow"),
            "Insert" => (16777222, "Insert"),
            "Delete" => (16777223, "Delete"),
            "Home" => (16777232, "Home"),
            "End" => (16777233, "End"),
            "PageUp" => (16777238, "Page Up"),
            "PageDown" => (16777239, "Page Down"),
            "F1" => (16777264, "F1"),
            "F2" => (16777265, "F2"),
            "F3" => (16777266, "F3"),
            "F4" => (16777267, "F4"),
            "F5" => (16777268, "F5"),
            "F6" => (16777269, "F6"),
            "F7" => (16777270, "F7"),
            "F8" => (16777271, "F8"),
            "F9" => (16777272, "F9"),
            "F10" => (16777273, "F10"),
            "F11" => (16777274, "F11"),
            "F12" => (16777275, "F12"),
            _ => CharKey(name),
        };
    }

    /// <summary>Friendly display name for a Qt key code (null when unknown).</summary>
    public static string? DisplayNameFor(int code)
    {
        if (code < 0) return "Not set";
        if (code >= 32 && code <= 126) return ((char)code).ToString();
        if (code >= 16777301 && code <= 16777310) return "Numpad " + (code - 16777301);
        return code switch
        {
            16777216 => "Esc",
            16777217 => "Tab",
            16777219 => "Backspace",
            16777220 => "Enter",
            16777222 => "Insert",
            16777223 => "Delete",
            16777232 => "Home",
            16777233 => "End",
            16777234 => "Left arrow",
            16777235 => "Up arrow",
            16777236 => "Right arrow",
            16777237 => "Down arrow",
            16777238 => "Page Up",
            16777239 => "Page Down",
            >= 16777264 and <= 16777275 => "F" + (code - 16777263),
            _ => $"Key {code}",
        };
    }
    // Letters, digits and punctuation share their ASCII codes in Qt.
    private static (int, string)? CharKey(string name)
    {
        if (name.Length == 1)
        {
            var c = name[0];
            if (c is >= 'A' and <= 'Z')
            {
                return (c, c.ToString());
            }
        }
        else if (name.Length == 2 && name[0] == 'D' && name[1] >= '0' && name[1] <= '9')
        {
            return (name[1], name[1].ToString());
        }
        else if (name.Length == 7 && name.StartsWith("NumPad"))
        {
            var digit = name[6];
            if (digit >= '0' && digit <= '9')
            {
                return (16777301 + (digit - '0'), "Numpad " + digit);
            }
        }
        else
        {
            // Punctuation: Avalonia names share their ASCII character in most cases.
            var (ascii, display) = name switch
            {
                "OemComma" => (44, "Comma"),
                "OemPeriod" => (46, "Period"),
                "OemMinus" => (45, "Minus"),
                "OemPlus" => (61, "Plus"),
                "OemSemicolon" => (59, "Semicolon"),
                "OemQuestion" => (47, "Slash"),
                "OemTilde" => (96, "Backtick"),
                "OemOpenBrackets" => (91, "Left bracket"),
                "OemCloseBrackets" => (93, "Right bracket"),
                "OemQuotes" => (39, "Apostrophe"),
                "OemPipe" => (92, "Backslash"),
                _ => (0, string.Empty),
            };
            if (display.Length > 0)
            {
                return (ascii, display);
            }
        }
        return null;
    }
}
