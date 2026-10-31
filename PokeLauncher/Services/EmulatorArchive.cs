using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace PokeLauncher.Services;

/// <summary>Result of extracting one archive into a target directory.</summary>
public sealed class ArchiveExtractResult
{
    /// <summary>Number of files written.</summary>
    public int FileCount { get; init; }

    /// <summary>Total uncompressed bytes written.</summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// When every entry sat inside one common top-level folder, that folder was stripped
    /// during extraction and this is its name; null when none was recognized.
    /// </summary>
    public string? FlattenedRootFolder { get; init; }
}

/// <summary>
/// Extraction engine for downloaded emulator archives. .zip uses the BCL
/// (System.IO.Compression); .7z uses SharpCompress (MIT). Streams entry-by-entry so
/// hostile archive paths are rejected before any bytes are written (zip-slip /
/// traversal protection), and so a single redundant top-level folder can be stripped.
/// </summary>
public static class EmulatorArchive
{
    /// <summary>Extracts an archive into a target directory the caller created.</summary>
    public static ArchiveExtractResult Extract(string archivePath, string format, string targetDir, CancellationToken cancellationToken)
    {
        return format?.Trim().ToLowerInvariant() switch
        {
            "zip" => ExtractZip(archivePath, targetDir, cancellationToken),
            "7z" => ExtractSevenZip(archivePath, targetDir, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported archive format '{format}'."),
        };
    }

    /// <summary>
    /// Maps the manifest's in-archive executable path onto the extracted directory,
    /// honoring top-folder flattening, and verifies the file really exists (the lookup is
    /// case-insensitive because zip/7z entry casing can differ). Returns the full path, or
    /// null with a user-displayable reason.
    /// </summary>
    public static string? ResolveExecutable(
        string targetDir,
        string executablePathInArchive,
        string? flattenedRootFolder,
        out string? problem)
    {
        problem = null;
        var relative = executablePathInArchive.Replace('\\', '/').Trim();
        if (flattenedRootFolder is { } root)
        {
            var prefix = root.TrimEnd('/') + "/";
            if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[prefix.Length..];
            }
            else
            {
                problem = $"The manifest executable '{executablePathInArchive}' does not match the downloaded archive's layout.";
                return null;
            }
        }
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is ".." or ".")
            {
                problem = $"The manifest executable '{executablePathInArchive}' is not a safe relative path.";
                return null;
            }
        }
        var rootPath = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        var expected = Path.GetFullPath(Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!expected.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            problem = $"The manifest executable '{executablePathInArchive}' resolves outside the install folder.";
            return null;
        }
        if (File.Exists(expected))
        {
            return expected;
        }

        // Archive entry casing can differ from the manifest; look for any file whose full
        // path ends with the same relative path, case-insensitively.
        var suffix = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
        {
            if (file.Length > suffix.Length &&
                file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        problem = $"The downloaded archive did not contain the expected executable '{executablePathInArchive}'.";
        return null;
    }

    private static ArchiveExtractResult ExtractZip(string archivePath, string targetDir, CancellationToken cancellationToken)
    {
        using var archiveStream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var entries = archive.Entries
            .Where(e => e.FullName.Length > 0 && !e.FullName.EndsWith("/"))
            .ToList();
        var (flattenSegment, _) = PlanEntryPaths(entries.Select(e => e.FullName));
        int files = 0;
        long written = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = NormalizeEntryPath(entry.FullName);
            if (normalized is null)
            {
                continue;
            }
            var relative = ApplyFlatten(normalized, flattenSegment);
            if (relative.Length == 0)
            {
                continue;
            }
            var target = BuildTargetPath(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var output = File.Create(target);
            source.CopyTo(output, 128 * 1024);
            written += entry.Length;
            files++;
        }
        return new ArchiveExtractResult
        {
            FileCount = files,
            TotalBytes = written,
            FlattenedRootFolder = flattenSegment,
        };
    }

    private static ArchiveExtractResult ExtractSevenZip(string archivePath, string targetDir, CancellationToken cancellationToken)
    {
        using var archive = SevenZipArchive.Open(archivePath, new ReaderOptions());
        var entries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Key) && !e.IsDirectory)
            .ToList();
        var (flattenSegment, _) = PlanEntryPaths(entries.Select(e => e.Key!));
        int files = 0;
        long written = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = NormalizeEntryPath(entry.Key!);
            if (normalized is null)
            {
                continue;
            }
            var relative = ApplyFlatten(normalized, flattenSegment);
            if (relative.Length == 0)
            {
                continue;
            }
            var target = BuildTargetPath(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.OpenEntryStream();
            using var output = File.Create(target);
            source.CopyTo(output, 128 * 1024);
            written += entry.Size;
            files++;
        }
        return new ArchiveExtractResult
        {
            FileCount = files,
            TotalBytes = written,
            FlattenedRootFolder = flattenSegment,
        };
    }

    /// <summary>
    /// Decides whether to strip a single shared top-level folder and returns the list of
    /// file paths (relative to the install folder) that should end up on disk, so the
    /// caller can report an accurate plan even when odd entries get skipped.
    /// </summary>
    private static (string? flattenSegment, List<string> files) PlanEntryPaths(IEnumerable<string> entryNames)
    {
        var safeNames = new List<string>();
        foreach (var name in entryNames)
        {
            var relative = NormalizeEntryPath(name);
            if (relative is not null)
            {
                safeNames.Add(relative);
            }
        }
        if (safeNames.Count == 0)
        {
            throw new IOException("The archive contains no safely-named file entries.");
        }

        var roots = safeNames
            .Select(n => n.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? flattenSegment = roots.Count == 1 && safeNames.Any(n => n.Contains('/'))
            ? roots[0]
            : null;

        var files = flattenSegment is null
            ? safeNames
            : safeNames
                .Select(n => n.StartsWith(flattenSegment + '/', StringComparison.OrdinalIgnoreCase)
                    ? n[(flattenSegment.Length + 1)..]
                    : n)
                .Where(n => n.Length > 0)
                .ToList();
        if (files.Count == 0)
        {
            throw new IOException("The archive contained only empty folders.");
        }
        return (flattenSegment, files.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Strips the shared top-level folder being flattened out of the install path.</summary>
    private static string ApplyFlatten(string relative, string? flattenSegment)
    {
        return flattenSegment is null || !relative.StartsWith(flattenSegment + '/', StringComparison.OrdinalIgnoreCase)
            ? relative
            : relative[(flattenSegment.Length + 1)..];
    }

    /// <summary>Rejects empty, rooted, traversal, and device-path entry names; null means rejected.</summary>
    private static string? NormalizeEntryPath(string entryName)
    {
        var unified = entryName.Replace('\\', '/').Trim();
        if (unified.Length == 0 ||
            Path.IsPathRooted(unified) ||
            unified.Contains(':') ||
            unified.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }
        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is ".." or "."))
        {
            return null;
        }
        return string.Join('/', segments);
    }

    private static string BuildTargetPath(string targetDir, string relative)
    {
        var target = Path.GetFullPath(Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Refusing to extract '{relative}' outside the install folder.");
        }
        return target;
    }
}
