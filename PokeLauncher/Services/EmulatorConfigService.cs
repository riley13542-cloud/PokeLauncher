using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLauncher.Services;

/// <summary>One mappable console button for a system.</summary>
public sealed record EmulatorButton(string Id, string Label);

/// <summary>
/// Reads and writes the keyboard-control sections of the emulators PokeLauncher
/// manages, plus a few melonDS display settings. Every write backs up the
/// emulator's configuration file first, and edits are limited to the input keys -
/// the rest of each configuration file is preserved.
/// </summary>
public sealed class EmulatorConfigService
{
    public record EmulatorConfigDefinition(
        string SystemKey,
        string ConfigFormat,      // "mgba-ini" | "melonds-toml"
        string ConfigFileName,
        IReadOnlyList<EmulatorButton> Buttons);

    private static readonly EmulatorButton[] GbaButtons =
    {
        new("A", "A"), new("B", "B"), new("L", "L"), new("R", "R"),
        new("Start", "Start"), new("Select", "Select"),
        new("Up", "Up"), new("Down", "Down"), new("Left", "Left"), new("Right", "Right"),
    };

    private static readonly EmulatorButton[] NdsButtons =
    {
        new("A", "A"), new("B", "B"), new("X", "X"), new("Y", "Y"),
        new("L", "L"), new("R", "R"),
        new("Start", "Start"), new("Select", "Select"),
        new("Up", "Up"), new("Down", "Down"), new("Left", "Left"), new("Right", "Right"),
    };

    private static readonly EmulatorConfigDefinition MgbaDefinition =
        new("GBA", "mgba-ini", "config.ini", GbaButtons);

    private static readonly EmulatorConfigDefinition MelondsDefinition =
        new("NDS", "melonds-toml", "melonDS.toml", NdsButtons);

    private static readonly Dictionary<string, EmulatorConfigDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GB"] = MgbaDefinition,
            ["GBC"] = MgbaDefinition,
            ["GBA"] = MgbaDefinition,
            ["NDS"] = MelondsDefinition,
        };

    public EmulatorConfigDefinition? GetDefinition(string systemKey) =>
        Definitions.TryGetValue(systemKey, out var definition) ? definition : null;

    public string? GetConfigPath(string systemKey, string exePath)
    {
        var definition = GetDefinition(systemKey);
        if (definition is null || string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }
        return Path.Combine(Path.GetDirectoryName(exePath)!, definition.ConfigFileName);
    }

    /// <summary>
    /// Backs up the emulator's configuration file once (before PokeLauncher ever
    /// modifies it), so the user's original setup can always be restored.
    /// </summary>
    public static void EnsureBackup(string configPath)
    {
        var backup = configPath + ".pokelauncher-backup";
        if (File.Exists(configPath) && !File.Exists(backup))
        {
            File.Copy(configPath, backup);
        }
    }

    // ---------------------------------------------------------- keyboard map

    /// <summary>Reads the current keyboard map (button id to Qt key code, -1 = unbound).</summary>
    public IReadOnlyDictionary<string, int> ReadKeyboardMap(string systemKey, string exePath)
    {
        var definition = GetDefinition(systemKey);
        var configPath = GetConfigPath(systemKey, exePath);
        var result = new Dictionary<string, int>();
        if (definition is null || configPath is null || !File.Exists(configPath))
        {
            return result;
        }

        try
        {
            var lines = File.ReadAllLines(configPath);
            foreach (var button in definition.Buttons)
            {
                result[button.Id] = definition.ConfigFormat switch
                {
                    "mgba-ini" => ReadMgbaKey(lines, button.Id),
                    "melonds-toml" => ReadMelondsKey(lines, button.Id),
                    _ => -1,
                };
            }
        }
        catch
        {
            /* unreadable config: return what we have */
        }
        return result;
    }

    /// <summary>
    /// Writes the keyboard map. For mGBA the rest of config.ini is preserved
    /// line-by-line; for melonDS the keyboard/display keys are updated in place.
    /// </summary>
    public void WriteKeyboardMap(string systemKey, string exePath, IReadOnlyDictionary<string, int> map)
    {
        var definition = GetDefinition(systemKey);
        var configPath = GetConfigPath(systemKey, exePath);
        if (definition is null || configPath is null)
        {
            return;
        }

        EnsureBackup(configPath);

        if (definition.ConfigFormat == "mgba-ini")
        {
            WriteMgbaKeyboardMap(configPath, map);
        }
        else
        {
            WriteMelondsKeyboardMap(configPath, map);
        }
    }

    // ---------------------------------------------------------- mGBA (config.ini)

    private const string MgbaSection = "[gba.input.QT_K]";

    private static int ReadMgbaKey(string[] lines, string buttonId)
    {
        var key = "key" + buttonId;
        var inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = string.Equals(line, MgbaSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || !line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq > 0 && int.TryParse(line[(eq + 1)..].Trim(), out var value))
            {
                return value;
            }
        }
        return -1;
    }

    private static void WriteMgbaKeyboardMap(string configPath, IReadOnlyDictionary<string, int> map)
    {
        var lines = new List<string>(File.Exists(configPath)
            ? File.ReadAllLines(configPath)
            : Array.Empty<string>());

        var managedKeys = map.Keys.Select(id => "key" + id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove managed key lines from wherever they are.
        lines.RemoveAll(line =>
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                return false;
            }
            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }
            var name = trimmed[..eq].Trim();
            return managedKeys.Contains(name);
        });

        // Append/refresh the keyboard section.
        var sectionIndex = lines.FindIndex(l => l.Trim().Equals(MgbaSection, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add(string.Empty);
            }
            lines.Add(MgbaSection);
            sectionIndex = lines.Count - 1;
        }

        var insertAt = sectionIndex + 1;
        foreach (var (buttonId, code) in map)
        {
            lines.Insert(insertAt++, "key" + buttonId + "=" + code);
        }

        File.WriteAllLines(configPath, lines);
    }

    // ---------------------------------------------------------- melonDS (melonDS.toml)

    private static readonly string[] MelondsButtons =
        { "A", "B", "X", "Y", "L", "R", "Start", "Select", "Up", "Down", "Left", "Right" };

    private static int ReadMelondsKey(string[] lines, string buttonId)
    {
        // Track the current TOML section so bare keys inside [Keyboard] or
        // [Instance0.Keyboard] resolve to their full dotted path. Instance0
        // (the first window's config) wins; fall back to the root key.
        var currentSection = string.Empty;
        var rootValue = -1;
        var instanceValue = -1;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var name = line[..eq].Trim();
            if (!int.TryParse(line[(eq + 1)..].Trim(), out var value))
            {
                continue;
            }
            if (string.Equals(name, buttonId, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(currentSection, "Instance0.Keyboard", StringComparison.OrdinalIgnoreCase))
                {
                    instanceValue = value;
                }
                else if (string.Equals(currentSection, "Keyboard", StringComparison.OrdinalIgnoreCase))
                {
                    rootValue = value;
                }
            }
        }
        return instanceValue >= 0 ? instanceValue : rootValue;
    }

    private static void WriteMelondsKeyboardMap(string configPath, IReadOnlyDictionary<string, int> map)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# melonDS configuration - keyboard controls managed by PokeLauncher.");
        sb.AppendLine("# A backup of the previous configuration is saved as melonDS.toml.pokelauncher-backup.");
        sb.AppendLine();

        sb.AppendLine("[Keyboard]");
        foreach (var button in MelondsButtons)
        {
            sb.AppendLine(button + " = " + (map.TryGetValue(button, out var code) ? code : -1));
        }
        sb.AppendLine();

        sb.AppendLine("[Instance0.Keyboard]");
        foreach (var button in MelondsButtons)
        {
            sb.AppendLine(button + " = " + (map.TryGetValue(button, out var code2) ? code2 : -1));
        }
        sb.AppendLine();

        sb.AppendLine("[Instance0.Joystick]");
        foreach (var button in MelondsButtons)
        {
            sb.AppendLine(button + " = -1");
        }
        sb.AppendLine();

        sb.AppendLine("[Emu]");
        sb.AppendLine("ConsoleType = 0");
        sb.AppendLine("DirectBoot = true");
        sb.AppendLine();

        sb.AppendLine("[Window0]");
        sb.AppendLine("ScreenLayout = 0");
        sb.AppendLine("ScreenSizing = 0");
        sb.AppendLine("ScreenSwap = false");
        sb.AppendLine("ScreenFilter = true");
        sb.AppendLine();

        sb.AppendLine("LimitFPS = true");
        sb.AppendLine("AudioSync = true");

        File.WriteAllText(configPath, sb.ToString());
    }

    // ---------------------------------------------------------- melonDS display settings

    public (int ScreenLayout, bool DirectBoot) ReadMelondsDisplaySettings(string exePath)
    {
        var configPath = Path.Combine(Path.GetDirectoryName(exePath) ?? string.Empty, "melonDS.toml");
        var layout = 0;
        var directBoot = true;
        if (File.Exists(configPath))
        {
            try
            {
                foreach (var raw in File.ReadAllLines(configPath))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("Window0.ScreenLayout", StringComparison.OrdinalIgnoreCase) && TryReadInt(line, out var l))
                    {
                        layout = l;
                    }
                    else if (line.StartsWith("Emu.DirectBoot", StringComparison.OrdinalIgnoreCase) && TryReadBool(line, out var b))
                    {
                        directBoot = b;
                    }
                }
            }
            catch
            {
                /* defaults */
            }
        }
        return (layout, directBoot);
    }

    public void WriteMelondsDisplaySettings(string exePath, int screenLayout, bool directBoot)
    {
        var configPath = Path.Combine(Path.GetDirectoryName(exePath) ?? string.Empty, "melonDS.toml");
        EnsureBackup(configPath);

        var lines = File.Exists(configPath)
            ? new List<string>(File.ReadAllLines(configPath))
            : new List<string>();

        // Update/insert the two managed keys (root-level dotted keys, valid anywhere
        // before any table header - we place them at the top to stay out of tables).
        lines.RemoveAll(l => l.TrimStart().StartsWith("Window0.ScreenLayout", StringComparison.OrdinalIgnoreCase)
                          || l.TrimStart().StartsWith("Emu.DirectBoot", StringComparison.OrdinalIgnoreCase));
        lines.Insert(0, "Window0.ScreenLayout = " + screenLayout);
        lines.Insert(0, "Emu.DirectBoot = " + (directBoot ? "true" : "false"));

        File.WriteAllLines(configPath, lines);
    }

    private static bool TryReadInt(string line, out int value)
    {
        var eq = line.IndexOf('=');
        value = 0;
        return eq > 0 && int.TryParse(line[(eq + 1)..].Trim(), out value);
    }

    private static bool TryReadBool(string line, out bool value)
    {
        var eq = line.IndexOf('=');
        value = false;
        if (eq <= 0)
        {
            return false;
        }
        var raw = line[(eq + 1)..].Trim().ToLowerInvariant();
        value = raw is "true" or "1";
        return true;
    }
}
