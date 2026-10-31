using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeLauncher.Models;

namespace PokeLauncher.Services;

/// <summary>What the import flow decided to do with one picked file.</summary>
public enum RomImportKind
{
    /// <summary>A brand-new library entry will be created for this file.</summary>
    New,
    /// <summary>The file clearly matches an existing game; its ROM path is attached there.</summary>
    Attach,
    /// <summary>This exact file (or an equivalent entry) is already in the library; skipped.</summary>
    Duplicate,
    /// <summary>The file cannot be imported (unsupported type, empty file…); skipped.</summary>
    Unsupported,
}

/// <summary>
/// The import flow's judgment about one picked file: what it is, what should happen to
/// it, and sensible defaults (title, platform, generation) the user can correct in the
/// review dialog before anything is written to the database.
/// </summary>
public sealed class RomFileAnalysis
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required RomImportKind Kind { get; init; }

    /// <summary>Human-readable explanation shown for Duplicate/Unsupported rows.</summary>
    public string? Note { get; init; }

    public required string SuggestedTitle { get; init; }

    public required string Platform { get; init; }

    public required int Generation { get; init; }

    /// <summary>Game the ROM should be attached to (Attach kind) or that already owns it (Duplicate).</summary>
    public PokemonGame? ExistingGame { get; init; }

    /// <summary>True for archives (.zip); they are launched as-is and the emulator must support them.</summary>
    public bool IsArchive { get; init; }
}

/// <summary>
/// Turns picked files into import decisions and new library rows. Detection is honest and
/// conservative: the platform is guessed from the file extension, a file is only "attached"
/// to a catalog game when its cleaned-up file name actually equals that game's title, and
/// a file whose exact path is already in the library is reported as a duplicate instead of
/// being imported twice. No ROM content is parsed or modified — files are only referenced.
/// </summary>
public sealed class RomImportService
{
    /// <summary>Extension → (platform, default generation guess). Kept in sync with EmulatorSystems.</summary>
    private static readonly Dictionary<string, (string Platform, int Generation)> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".gb"] = ("Game Boy", 1),
            [".gbc"] = ("Game Boy Color", 2),
            [".gba"] = ("Game Boy Advance", 3),
            [".nds"] = ("Nintendo DS", 4),
        };

    /// <summary>Archives are accepted because mGBA-family emulators open them directly.</summary>
    private static readonly Dictionary<string, (string Platform, int Generation)> ArchiveMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".zip"] = ("Game Boy Advance", 3),
        };

    public static string SupportedExtensionsText => ".gb, .gbc, .gba, .nds, .zip";

    /// <summary>File-picker filter for the import dialog.</summary>
    public static IReadOnlyList<string> KnownExtensions { get; } =
        ExtensionMap.Keys.Concat(ArchiveMap.Keys).ToArray();

    /// <summary>
    /// Analyzes one file. <paramref name="games"/> is the current library, used for
    /// catalog matching and duplicate detection.
    /// </summary>
    public RomFileAnalysis AnalyzeFile(string filePath, IReadOnlyList<PokemonGame> games)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        if (!File.Exists(filePath))
        {
            return Unsupported(filePath, fileName, "The file no longer exists at that location.");
        }

        if (new FileInfo(filePath).Length == 0)
        {
            return Unsupported(filePath, fileName, "The file is empty (0 bytes) and cannot be a game.");
        }

        if (ExtensionMap.TryGetValue(extension, out var detected))
        {
            return AnalyzeRegularFile(filePath, fileName, games, detected.Platform, detected.Generation, isArchive: false);
        }
        if (ArchiveMap.TryGetValue(extension, out var archive))
        {
            return AnalyzeRegularFile(filePath, fileName, games, archive.Platform, archive.Generation, isArchive: true);
        }

        return Unsupported(filePath, fileName,
            $"'{extension}' is not a supported game file type. Supported: {SupportedExtensionsText}.");
    }

    private RomFileAnalysis AnalyzeRegularFile(
        string filePath,
        string fileName,
        IReadOnlyList<PokemonGame> games,
        string platform,
        int generation,
        bool isArchive)
    {
        // A file whose path is already recorded in the library is a duplicate, even if
        // its name also matches another game — the library can only hold one entry per file.
        var pathOwner = games.FirstOrDefault(g =>
            g.RomPath is not null &&
            string.Equals(g.RomPath, filePath, StringComparison.OrdinalIgnoreCase));
        if (pathOwner is not null)
        {
            return new RomFileAnalysis
            {
                FilePath = filePath,
                FileName = fileName,
                Kind = RomImportKind.Duplicate,
                Note = $"Already in the library as “{pathOwner.Title}”.",
                SuggestedTitle = CleanTitle(fileName),
                Platform = platform,
                Generation = generation,
                ExistingGame = pathOwner,
                IsArchive = isArchive,
            };
        }

        var suggestedTitle = CleanTitle(fileName);
        var normalizedTitle = NormalizeTitle(suggestedTitle);

        // A catalog entry with this exact title wins: the file is linked to that game.
        var catalogMatch = games.FirstOrDefault(g =>
            !g.IsImported && NormalizeTitle(g.Title) == normalizedTitle);
        if (catalogMatch is not null)
        {
            return new RomFileAnalysis
            {
                FilePath = filePath,
                FileName = fileName,
                Kind = RomImportKind.Attach,
                Note = $"Looks like “{catalogMatch.Title}” — the file will be linked to that game.",
                SuggestedTitle = suggestedTitle,
                Platform = platform,
                Generation = catalogMatch.Generation,
                ExistingGame = catalogMatch,
                IsArchive = isArchive,
            };
        }

        // Same title AND platform imported before? Then this is a redundant re-import of
        // a game the user already added — skipped so the existing ROM link is never
        // silently replaced.
        var importedTwin = games.FirstOrDefault(g =>
            g.IsImported &&
            g.Platform == platform &&
            NormalizeTitle(g.Title) == normalizedTitle);
        if (importedTwin is not null)
        {
            return new RomFileAnalysis
            {
                FilePath = filePath,
                FileName = fileName,
                Kind = RomImportKind.Duplicate,
                Note = $"“{importedTwin.Title}” is already in the library for {platform}.",
                SuggestedTitle = suggestedTitle,
                Platform = platform,
                Generation = generation,
                ExistingGame = importedTwin,
                IsArchive = isArchive,
            };
        }

        return new RomFileAnalysis
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = RomImportKind.New,
            SuggestedTitle = suggestedTitle,
            Platform = platform,
            Generation = generation,
            IsArchive = isArchive,
        };
    }

    private static RomFileAnalysis Unsupported(string filePath, string fileName, string note) =>
        new()
        {
            FilePath = filePath,
            FileName = fileName,
            Kind = RomImportKind.Unsupported,
            Note = note,
            SuggestedTitle = Path.GetFileNameWithoutExtension(fileName),
            Platform = string.Empty,
            Generation = 0,
        };

    /// <summary>
    /// Builds the database row for a confirmed "new game" import. Cover colors come from a
    /// fixed palette chosen by a stable hash of the title, so entries vary but never flicker.
    /// </summary>
    public PokemonGame BuildImportedGame(string title, string platform, int generation, string romPath, bool isArchive)
    {
        var (fromHex, toHex) = CoverPalette[Math.Abs(StableHash(title)) % CoverPalette.Count];
        var fileName = Path.GetFileName(romPath);
        return new PokemonGame
        {
            Id = "imported-" + Guid.NewGuid().ToString("N")[..12],
            Title = title,
            Generation = generation,
            Platform = platform,
            ReleaseYear = 0,
            Description = isArchive
                ? $"Imported archive: {fileName}. The emulator opens archives directly. Edit the details to describe this game."
                : $"Imported ROM file: {fileName}. Edit the details to describe this game.",
            CoverFromHex = fromHex,
            CoverToHex = toHex,
            IsImported = true,
            RomPath = romPath,
        };
    }

    /// <summary>
    /// Cleans a file name into a suggested title: drops the extension, region tags like
    /// "(U)" / "[!]" / "(Rev 1)", and a trailing "Version" word.
    /// </summary>
    public static string CleanTitle(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Trim();
        // Strip bracketed/parenthesised tags anywhere in the name: (U), [!] (Rev 1)…
        while (true)
        {
            var open = title.LastIndexOfAny(new[] { '(', '[' });
            if (open < 0)
            {
                break;
            }
            var closeChar = title[open] == '(' ? ')' : ']';
            var close = title.IndexOf(closeChar, open);
            if (close < 0)
            {
                break;
            }
            title = title.Remove(open, close - open + 1).Trim();
        }
        title = title.TrimEnd('-', '_', ' ');
        if (title.EndsWith("version", StringComparison.OrdinalIgnoreCase))
        {
            title = title[..^"version".Length].TrimEnd('-', '_', ' ');
        }
        return title.Length > 0 ? title : Path.GetFileNameWithoutExtension(fileName);
    }

    private static string NormalizeTitle(string title)
    {
        var lowered = title.ToLowerInvariant().Replace('é', 'e');
        if (lowered.StartsWith("pokemon", StringComparison.Ordinal))
        {
            lowered = lowered["pokemon".Length..];
        }
        if (lowered.EndsWith("version", StringComparison.Ordinal))
        {
            lowered = lowered[..^"version".Length];
        }
        var builder = new System.Text.StringBuilder(lowered.Length);
        foreach (var character in lowered)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in text)
            {
                hash = hash * 31 + character;
            }
            return hash;
        }
    }

    /// <summary>Gradient pairs in the same vibrant style as the seed catalog covers.</summary>
    private static readonly IReadOnlyList<(string From, string To)> CoverPalette = new[]
    {
        ("#F59E0B", "#7C2D12"),
        ("#10B981", "#064E3B"),
        ("#3B82F6", "#1E3A8A"),
        ("#EC4899", "#831843"),
        ("#8B5CF6", "#2E1065"),
        ("#14B8A6", "#134E4A"),
        ("#F43F5E", "#881337"),
        ("#84CC16", "#365314"),
    };
}
