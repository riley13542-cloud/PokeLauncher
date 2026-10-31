using System;
using System.IO;
using System.Linq;

namespace PokeLauncher.Services;

/// <summary>
/// PokeLauncher's managed data folders under %LocalAppData%\PokeLauncher:
/// - ROMs\<game-id>\  — one folder per game holding the managed copy of an imported ROM.
///   Emulators write their save files next to the ROM they were launched with, so a game's
///   live saves also land in this folder automatically.
/// - Saves\<game-id>\ — timestamped backup snapshots of those save files.
/// The original file a user imported is never moved, modified, or deleted by PokeLauncher.
/// </summary>
public static class ManagedLibrary
{
    public static string RomsRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLauncher", "ROMs");

    public static string SavesRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLauncher", "Saves");

    /// <summary>Whether a game's ROM lives inside the managed library.</summary>
    public static bool IsManagedPath(string? romPath) =>
        !string.IsNullOrWhiteSpace(romPath) &&
        romPath.StartsWith(RomsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies a picked ROM file into the game's managed folder and returns the managed
    /// path. An existing managed copy of the same game is replaced — the source file is
    /// only ever read. Throws on IO failure; callers surface that honestly.
    /// </summary>
    public static string CopyIntoManagedLibrary(string sourcePath, string gameId)
    {
        var gameFolder = Path.Combine(RomsRoot, Sanitize(gameId));
        Directory.CreateDirectory(gameFolder);
        var target = Path.Combine(gameFolder, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    /// <summary>Deletes a game's managed ROM folder (managed copies only, by id).</summary>
    public static void DeleteManagedRom(string gameId)
    {
        var gameFolder = Path.Combine(RomsRoot, Sanitize(gameId));
        if (Directory.Exists(gameFolder) &&
            gameFolder.StartsWith(RomsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            try { Directory.Delete(gameFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Staging folder: users drop their own ROM files here; the import flow
    /// matches them against the catalog and moves them into managed storage.</summary>
    public static string InboxRoot => Path.Combine(RomsRoot, "Inbox");

    /// <summary>Cover art lives as Covers\<game-id>.png/.jpg/etc in the data folder.</summary>
    public static string CoversRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLauncher", "Covers");

    /// <summary>Optional user-supplied brand logo; overrides the bundled app icon.</summary>
    public static string LogoPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLauncher", "logo.png");

    /// <summary>
    /// The brand logo image: a user-supplied logo.png in the data folder when present,
    /// otherwise the bundled app emblem. Null only if the assets are unreadable.
    /// </summary>
    public static Avalonia.Media.Imaging.Bitmap? LoadLogo()
    {
        try
        {
            if (File.Exists(LogoPath))
            {
                using var stream = File.OpenRead(LogoPath);
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }
        }
        catch
        {
            /* fall through to the bundled emblem */
        }
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://PokeLauncher/Assets/app-icon.png"));
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether a user-supplied logo.png exists in the data folder.</summary>
    public static bool LogoFileExists() => File.Exists(LogoPath);

    /// <summary>Existing cover image for a game, or null. Order: png, jpg, jpeg, webp, bmp.</summary>
    public static string? FindCover(string gameId)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
        {
            var candidate = Path.Combine(CoversRoot, gameId + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Copies an image into Covers\<game-id>.<ext>, replacing any previous cover.</summary>
    public static string SetCover(string sourcePath, string gameId)
    {
        Directory.CreateDirectory(CoversRoot);
        foreach (var known in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
        {
            var previous = Path.Combine(CoversRoot, gameId + known);
            if (File.Exists(previous))
            {
                File.Delete(previous);
            }
        }
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".bmp")
        {
            extension = ".png";
        }
        var target = Path.Combine(CoversRoot, gameId + extension);
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    /// <summary>Removes a game's cover file, if any.</summary>
    public static void DeleteCover(string gameId)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
        {
            var cover = Path.Combine(CoversRoot, gameId + extension);
            if (File.Exists(cover))
            {
                try { File.Delete(cover); } catch { /* best effort */ }
            }
        }
    }
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "_" : cleaned;
    }
}
