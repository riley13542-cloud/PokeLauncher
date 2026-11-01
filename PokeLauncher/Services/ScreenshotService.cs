using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLauncher.Services;

/// <summary>
/// Discovers emulator screenshots for a game. Both mGBA and melonDS name screenshot
/// files after the running ROM (e.g. "emerald-1.png"), so scanning the emulators'
/// folders for images whose file name contains the ROM's base name finds them
/// regardless of which subfolder each emulator writes to.
/// </summary>
public sealed class ScreenshotService
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    private readonly AppDatabase _database;

    public ScreenshotService(AppDatabase database)
    {
        _database = database;
    }

    /// <summary>Screenshot file paths for a ROM, newest first (empty when none found).</summary>
    public IReadOnlyList<string> ForRom(string? romPath)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
        {
            return results;
        }

        var baseName = Path.GetFileNameWithoutExtension(romPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return results;
        }

        var folders = _database.LoadEmulatorConfigs().Values
            .Select(config => config.ExecutablePath)
            .Where(exe => !string.IsNullOrWhiteSpace(exe))
            .Select(exe => Path.GetDirectoryName(exe!))
            .OfType<string>()
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in folders)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var name = Path.GetFileName(file);
                    if (name.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                    }
                }
            }
            catch
            {
                /* an unreadable emulator folder is skipped, never fatal */
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTime)
            .ToList();
    }
}
