using System;
using System.Collections.Generic;
using System.IO;

namespace PokeLauncher.Services;

/// <summary>
/// Known command-line flags for emulators PokeLauncher can launch, keyed by the
/// emulator's executable file name. Used by the Settings launch-option toggles and
/// the launcher. Flags unknown to this table are set through the manual arguments
/// field on the emulator card.
/// </summary>
public static class EmulatorLaunchFlags
{
    /// <summary>Fullscreen flag per emulator executable (e.g. "mgba.exe" → "--fullscreen").</summary>
    public static readonly IReadOnlyDictionary<string, string> Fullscreen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mgba.exe"] = "--fullscreen",
        ["melonDS.exe"] = "--fullscreen",
    };

    /// <summary>The fullscreen flag for a configured emulator executable, or null when unknown.</summary>
    public static string? FullscreenFlagFor(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }
        var name = Path.GetFileName(executablePath.Trim());
        return Fullscreen.TryGetValue(name, out var flag) ? flag : null;
    }

    /// <summary>Whether the argument template already contains the flag as a standalone token.</summary>
    public static bool HasFlag(string arguments, string flag)
    {
        foreach (var token in arguments.Split(' '))
        {
            if (string.Equals(token, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Removes every occurrence of the flag token from the template.</summary>
    public static string WithoutFlag(string arguments, string flag)
    {
        var result = arguments;
        result = result.Replace(flag + " ", string.Empty, StringComparison.OrdinalIgnoreCase);
        result = result.Replace(" " + flag, string.Empty, StringComparison.OrdinalIgnoreCase);
        result = result.Replace(flag, string.Empty, StringComparison.OrdinalIgnoreCase);
        return result.Trim();
    }
}
