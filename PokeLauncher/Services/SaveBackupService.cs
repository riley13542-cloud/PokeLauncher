using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeLauncher.Models;

namespace PokeLauncher.Services;

/// <summary>
/// Save-file backups for one game. Emulators write their saves next to the ROM they were
/// launched with, so the backup source is the ROM's own folder. Snapshots are timestamped
/// folders under Saves\<game-id>\ — they are purely additive, so creating one never
/// overwrites anything. Restoring first takes a safety snapshot of the current state, then
/// copies the chosen snapshot's files back. The last <see cref="MaxBackupsPerGame"/> are
/// kept; older ones are pruned automatically. The original imported ROM file is never touched.
/// </summary>
public sealed class SaveBackupService
{
    /// <summary>Save/battery/state file extensions PokeLauncher snapshots.</summary>
    public static readonly IReadOnlyList<string> SaveExtensions = new[]
    {
        ".sav", ".sav2", ".sa1", ".sa2", ".srm", ".dsv", ".rtc",
        ".ss0", ".ss1", ".ss2", ".ss3", ".ss4", ".ss5", ".ss6", ".ss7", ".ss8", ".ss9",
    };

    public const int DefaultBackupsPerGame = 20;

    /// <summary>User-configurable cap (Settings → save backups kept per game).</summary>
    public int Retention { get; set; } = DefaultBackupsPerGame;

    /// <summary>Save files currently sitting next to a game's ROM (live, un-snapshotted).</summary>
    public IReadOnlyList<string> ListLiveSaveFiles(string romPath)
    {
        var files = new List<string>();
        var directory = Path.GetDirectoryName(romPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || string.IsNullOrEmpty(romPath))
        {
            return files;
        }
        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                if (string.Equals(file, romPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (SaveExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    files.Add(file);
                }
            }
        }
        catch
        {
            /* unreadable folder: report "no saves" rather than failing the caller */
        }
        return files;
    }

    /// <summary>All snapshots for a game, newest first.</summary>
    public IReadOnlyList<SaveBackup> ListBackups(string gameId)
    {
        var backups = new List<SaveBackup>();
        var gameSaves = Path.Combine(ManagedLibrary.SavesRoot, gameId);
        if (!Directory.Exists(gameSaves))
        {
            return backups;
        }
        try
        {
            foreach (var folder in Directory.GetDirectories(gameSaves))
            {
                var name = Path.GetFileName(folder);
                if (!TryParseTimestamp(name, out var createdAt))
                {
                    continue;
                }
                var files = Directory.GetFiles(folder);
                backups.Add(new SaveBackup
                {
                    Name = name,
                    CreatedAt = createdAt,
                    Folder = folder,
                    Files = files.Select(Path.GetFileName).OfType<string>().ToList(),
                    TotalBytes = files.Sum(f => new FileInfo(f).Length),
                });
            }
        }
        catch
        {
            /* unreadable saves folder: show what we could */
        }
        // Folder names sort chronologically (zero-padded timestamp + "(2)"-style suffixes),
        // so a plain descending name sort is both newest-first and deterministic.
        return backups.OrderByDescending(b => b.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Snapshots the game's current save files (if any) into a timestamped folder.
    /// Returns null when there was nothing to back up; throws on IO failure so callers
    /// can surface the problem.
    /// </summary>
    public SaveBackup? CreateBackup(string gameId, string romPath)
    {
        var saveFiles = ListLiveSaveFiles(romPath);
        if (saveFiles.Count == 0)
        {
            return null;
        }

        var createdAt = DateTime.Now;
        var name = createdAt.ToString("yyyy-MM-dd_HH-mm-ss");
        var target = Path.Combine(ManagedLibrary.SavesRoot, gameId, name);
        // Same-second collisions: make the folder name unique instead of merging snapshots.
        var suffix = 1;
        while (Directory.Exists(target))
        {
            target = Path.Combine(ManagedLibrary.SavesRoot, gameId, $"{name} ({++suffix})");
        }
        Directory.CreateDirectory(target);

        long total = 0;
        var copied = new List<string>();
        foreach (var file in saveFiles)
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: false);
            total += new FileInfo(destination).Length;
            copied.Add(Path.GetFileName(file));
        }

        PruneOldBackups(gameId);
        return new SaveBackup
        {
            Name = Path.GetFileName(target),
            CreatedAt = createdAt,
            Folder = target,
            Files = copied,
            TotalBytes = total,
        };
    }

    /// <summary>
    /// Restores a snapshot: first snapshots the current state (a safety copy the user can
    /// roll back to), then copies the snapshot's files next to the ROM. Returns the
    /// safety-snapshot, or null when the restore restored nothing.
    /// </summary>
    public SaveBackup? Restore(string gameId, SaveBackup backup, string romPath)
    {
        if (!File.Exists(romPath))
        {
            throw new InvalidOperationException("The ROM file is missing, so there is no save folder to restore into.");
        }
        var safety = CreateBackup(gameId, romPath);

        var romDirectory = Path.GetDirectoryName(romPath)!;
        Directory.CreateDirectory(romDirectory);
        foreach (var file in backup.Files)
        {
            File.Copy(Path.Combine(backup.Folder, file), Path.Combine(romDirectory, file), overwrite: true);
        }
        return safety;
    }

    /// <summary>Deletes one snapshot folder. Returns false when it was already gone.</summary>
    public bool DeleteBackup(string gameId, SaveBackup backup)
    {
        var gameSaves = Path.Combine(ManagedLibrary.SavesRoot, gameId);
        var folder = Path.GetFullPath(backup.Folder);
        // Only ever delete inside our own Saves root.
        if (!folder.StartsWith(gameSaves + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!Directory.Exists(folder))
        {
            return false;
        }
        Directory.Delete(folder, recursive: true);
        return true;
    }

    private void PruneOldBackups(string gameId)
    {
        var backups = ListBackups(gameId);
        foreach (var old in backups.Skip(Retention))
        {
            try { Directory.Delete(old.Folder, recursive: true); } catch { /* best effort */ }
        }
    }

    private static bool TryParseTimestamp(string name, out DateTime value)
    {
        // Snapshot folders are "yyyy-MM-dd_HH-mm-ss"; same-second collisions get a
        // " (2)"-style suffix that must not make the snapshot invisible.
        var trimmed = name;
        var open = name.IndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && name.EndsWith(")", StringComparison.Ordinal))
        {
            trimmed = name[..open];
        }
        return DateTime.TryParseExact(trimmed, "yyyy-MM-dd_HH-mm-ss", null,
            System.Globalization.DateTimeStyles.None, out value);
    }
}
