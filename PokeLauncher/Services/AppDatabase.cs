using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PokeLauncher.Data;
using PokeLauncher.Models;

namespace PokeLauncher.Services;

/// <summary>
/// Local SQLite store for everything that must survive restarts: catalog games,
/// user library state (favorites, ROM paths, play stats), ROM hack bookmarks,
/// collections, emulator configurations, and general settings.
///
/// The database lives in %LocalAppData%\PokeLauncher\library.db — deliberately outside
/// OneDrive-synced project folders so sync conflicts cannot corrupt user data.
///
/// All operations "fail soft": if the store cannot be opened, the app keeps running with
/// catalog defaults in memory and <see cref="LoadError"/> explains why data will not persist.
/// Failed writes never overwrite valid persisted data with empty sets.
/// </summary>
public sealed class AppDatabase
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public AppDatabase()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeLauncher", "library.db"))
    {
    }

    /// <summary>Test/alternative-location constructor: injects the database file path.</summary>
    public AppDatabase(string databasePath)
    {
        _dbPath = databasePath;
    }

    /// <summary>Full path of the SQLite database file, shown on the Settings page.</summary>
    public string DatabasePath => _dbPath;

    /// <summary>Non-null when the database could not be opened (degraded in-memory mode).</summary>
    public string? LoadError { get; private set; }

    public bool IsPersistent => _connection is not null;

    // ------------------------------------------------------------------ lifecycle

    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            Execute("PRAGMA journal_mode = WAL");
            Execute("PRAGMA foreign_keys = ON");
            EnsureSchema();
            MigrateSchema();
            SeedIfEmpty();
            LoadError = null;
        }
        catch (Exception exception)
        {
            // Leave the app usable with catalog defaults; never half-write a broken database.
            TryClose();
            LoadError = exception.Message;
        }
    }

    /// <summary>
    /// In-place upgrades for databases created by older PokeLauncher versions. New tables
    /// are handled by <see cref="EnsureSchema"/> (CREATE TABLE IF NOT EXISTS); this adds
    /// columns that appeared after the 0.5/0.6 schema. Never drops or rewrites data.
    /// </summary>
    private void MigrateSchema()
    {
        var gameColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = _connection!.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(games)";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                gameColumns.Add(reader.GetString(1));
            }
        }

        // 0.7.0: distinguish imported games from seeded catalog entries.
        if (!gameColumns.Contains("is_imported"))
        {
            Execute("ALTER TABLE games ADD COLUMN is_imported INTEGER NOT NULL DEFAULT 0");
        }

        // 0.10.0: remember when a ROM was imported (details-page "Added" row).
        if (!gameColumns.Contains("imported_at"))
        {
            Execute("ALTER TABLE games ADD COLUMN imported_at TEXT");
        }
    }

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS games (
                id             TEXT PRIMARY KEY,
                title          TEXT NOT NULL,
                generation     INTEGER NOT NULL,
                platform       TEXT NOT NULL,
                release_year   INTEGER NOT NULL,
                description    TEXT NOT NULL,
                cover_from     TEXT NOT NULL,
                cover_to       TEXT NOT NULL,
                is_favorite    INTEGER NOT NULL DEFAULT 0,
                rom_path       TEXT,
                last_played    TEXT,
                playtime_hours REAL NOT NULL DEFAULT 0,
                is_imported    INTEGER NOT NULL DEFAULT 0,
                imported_at    TEXT
            );

            CREATE TABLE IF NOT EXISTS play_sessions (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                game_id          TEXT NOT NULL REFERENCES games(id) ON DELETE CASCADE,
                started_at       TEXT NOT NULL,
                ended_at         TEXT NOT NULL,
                duration_seconds REAL NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_play_sessions_game
                ON play_sessions(game_id);

            CREATE TABLE IF NOT EXISTS rom_hacks (
                id           TEXT PRIMARY KEY,
                title        TEXT NOT NULL,
                base_game_id TEXT NOT NULL REFERENCES games(id),
                description  TEXT NOT NULL DEFAULT '',
                creator      TEXT NOT NULL DEFAULT '',
                url          TEXT NOT NULL DEFAULT '',
                rom_path     TEXT,
                genre        TEXT NOT NULL DEFAULT 'Exploration',
                difficulty   TEXT NOT NULL DEFAULT 'Medium',
                status       TEXT NOT NULL DEFAULT 'In Development',
                created_at   TEXT
            );

            CREATE TABLE IF NOT EXISTS hack_bookmarks (
                hack_id    TEXT PRIMARY KEY,
                bookmarked INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS collections (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                description TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS collection_games (
                collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                game_id       TEXT NOT NULL REFERENCES games(id) ON DELETE CASCADE,
                PRIMARY KEY (collection_id, game_id)
            );

            CREATE TABLE IF NOT EXISTS emulator_configs (
                system_key      TEXT PRIMARY KEY,
                executable_path TEXT NOT NULL DEFAULT '',
                arguments       TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS emulator_downloads (
                emulator_id     TEXT PRIMARY KEY,
                version         TEXT NOT NULL,
                sha256          TEXT NOT NULL,
                source_url      TEXT NOT NULL,
                install_folder  TEXT NOT NULL,
                executable_path TEXT NOT NULL,
                downloaded_at   TEXT NOT NULL
            );
            """);
    }

    private void SeedIfEmpty()
    {
        if (Scalar("SELECT COUNT(*) FROM games") == 0)
        {
            var games = MockCatalog.CreateGames();
            using var transaction = _connection!.BeginTransaction();
            foreach (var game in games)
            {
                UpsertGame(game, transaction);
            }
            transaction.Commit();
        }

        // One row per supported emulator system so Settings always shows the full list.
        if (Scalar("SELECT COUNT(*) FROM emulator_configs") == 0)
        {
            using var transaction = _connection!.BeginTransaction();
            foreach (var system in EmulatorSystems.All)
            {
                UpsertEmulatorConfigCore(system.Key, string.Empty, string.Empty, transaction);
            }
            transaction.Commit();
        }

        // Collections seed exactly once. The flag makes a deliberately emptied
        // collections list survive restarts instead of being re-seeded.
        if (GetSetting("collections.seeded") is null && Scalar("SELECT COUNT(*) FROM collections") == 0)
        {
            using var transaction = _connection!.BeginTransaction();
            foreach (var collection in MockCatalog.CreateCollections())
            {
                UpsertCollection(collection, transaction);
            }
            transaction.Commit();
        }
        if (GetSetting("collections.seeded") is null)
        {
            SetSetting("collections.seeded", "1");
        }
    }

    private void TryClose()
    {
        try
        {
            _connection?.Dispose();
        }
        catch
        {
            /* best effort */
        }
        _connection = null;
    }

    // ------------------------------------------------------------------ games

    /// <summary>
    /// Loads catalog games plus user state. Falls back to the seed catalog when the
    /// database is unavailable; the fallback never includes fake progress data.
    /// </summary>
    public IReadOnlyList<PokemonGame> LoadGames()
    {
        if (_connection is null)
        {
            return MockCatalog.CreateGames();
        }

        try
        {
            var games = new List<PokemonGame>();
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, generation, platform, release_year, description,
                       cover_from, cover_to, is_favorite, rom_path, last_played, playtime_hours,
                       is_imported, imported_at
                FROM games
                ORDER BY title COLLATE NOCASE
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                games.Add(MapGame(reader));
            }
            return games.Count > 0 ? games : MockCatalog.CreateGames();
        }
        catch (Exception exception)
        {
            LoadError = $"Reading games failed: {exception.Message}";
            return MockCatalog.CreateGames();
        }
    }

    public void SaveGameFavorite(string gameId, bool isFavorite) =>
        ExecuteNonQuery(
            "UPDATE games SET is_favorite = $value WHERE id = $id",
            ("$id", gameId),
            ("$value", isFavorite ? 1 : 0));

    public void SaveGameRomPath(string gameId, string? romPath) =>
        ExecuteNonQuery(
            "UPDATE games SET rom_path = $value WHERE id = $id",
            ("$id", gameId),
            ("$value", (object?)romPath ?? DBNull.Value));

    public void SaveGamePlayState(string gameId, DateTime? lastPlayed, double playtimeHours) =>
        ExecuteNonQuery(
            """
            UPDATE games
            SET last_played = $lastPlayed, playtime_hours = $hours
            WHERE id = $id
            """,
            ("$id", gameId),
            ("$lastPlayed", (object?)lastPlayed?.ToString("O") ?? DBNull.Value),
            ("$hours", playtimeHours));

    /// <summary>
    /// Inserts a new game row created by the ROM import flow (full insert, unlike the
    /// seed upsert). Returns false when the database is unavailable or the insert failed.
    /// </summary>
    public bool AddImportedGame(PokemonGame game)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO games
                    (id, title, generation, platform, release_year, description,
                     cover_from, cover_to, is_favorite, rom_path, last_played, playtime_hours,
                     is_imported, imported_at)
                VALUES
                    ($id, $title, $generation, $platform, $releaseYear, $description,
                     $coverFrom, $coverTo, 0, $romPath, NULL, 0, 1, $importedAt)
                """;
            FillGameParameters(command, game);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            LoadError = $"Adding imported game failed: {exception.Message}";
            return false;
        }
    }

    /// <summary>Updates user-editable metadata (title, platform, generation, description).</summary>
    public void SaveGameMetadata(PokemonGame game)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE games
                SET title = $title, platform = $platform,
                    generation = $generation, description = $description
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$title", game.Title);
            command.Parameters.AddWithValue("$platform", game.Platform);
            command.Parameters.AddWithValue("$generation", game.Generation);
            command.Parameters.AddWithValue("$description", game.Description);
            command.Parameters.AddWithValue("$id", game.Id);
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Saving game metadata failed: {exception.Message}";
        }
    }

    /// <summary>Looks up a game by its recorded ROM path (case-insensitive full path).</summary>
    public PokemonGame? FindGameByRomPath(string romPath)
    {
        if (_connection is null)
        {
            return null;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, generation, platform, release_year, description,
                       cover_from, cover_to, is_favorite, rom_path, last_played, playtime_hours,
                       is_imported, imported_at
                FROM games
                WHERE rom_path IS NOT NULL AND lower(rom_path) = lower($path)
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$path", romPath);
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapGame(reader) : null;
        }
        catch (Exception exception)
        {
            LoadError = $"Looking up ROM path failed: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Removes a game row entirely (imported games only). Collection links cascade.
    /// The original ROM file on disk is never touched. Returns false on failure.
    /// </summary>
    public bool DeleteGame(string gameId)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM games WHERE id = $id AND is_imported = 1";
            command.Parameters.AddWithValue("$id", gameId);
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception exception)
        {
            LoadError = $"Removing game failed: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Records one completed play session and returns its row id, or 0 when the database
    /// is unavailable. The caller separately updates the game's aggregate play state.
    /// </summary>
    public long InsertPlaySession(PlaySessionRecord session)
    {
        if (_connection is null)
        {
            return 0;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO play_sessions (game_id, started_at, ended_at, duration_seconds)
                VALUES ($gameId, $startedAt, $endedAt, $duration);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$gameId", session.GameId);
            command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$endedAt", session.EndedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$duration", session.DurationSeconds);
            return (long)(command.ExecuteScalar() ?? 0L);
        }
        catch (Exception exception)
        {
            LoadError = $"Saving play session failed: {exception.Message}";
            return 0;
        }
    }

    /// <summary>Most recent sessions for a game, newest first (details-page activity).</summary>
    public IReadOnlyList<PlaySessionRecord> LoadPlaySessionsForGame(string gameId, int limit = 10)
    {
        var sessions = new List<PlaySessionRecord>();
        if (_connection is null)
        {
            return sessions;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, game_id, started_at, ended_at, duration_seconds
                FROM play_sessions
                WHERE game_id = $gameId
                ORDER BY started_at DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$gameId", gameId);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime startedAt = reader.GetDateTime(2);
                DateTime endedAt = reader.GetDateTime(3);
                sessions.Add(new PlaySessionRecord
                {
                    Id = reader.GetInt64(0),
                    GameId = reader.GetString(1),
                    StartedAt = startedAt,
                    EndedAt = endedAt,
                    DurationSeconds = reader.GetDouble(4),
                });
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading play sessions failed: {exception.Message}";
        }
        return sessions;
    }

    /// <summary>Total recorded sessions for a game (details-page activity caption).</summary>
    public long CountPlaySessions(string gameId)
    {
        if (_connection is null)
        {
            return 0;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM play_sessions WHERE game_id = $gameId";
            command.Parameters.AddWithValue("$gameId", gameId);
            return (long)(command.ExecuteScalar() ?? 0L);
        }
        catch (Exception exception)
        {
            LoadError = $"Counting play sessions failed: {exception.Message}";
            return 0;
        }
    }

    // ---------------------------------------------------------- playtime dashboard

    /// <summary>Per-game playtime totals, most-played first (Stats page).</summary>
    public IReadOnlyList<PlaytimeByGameRow> LoadPlaytimeByGame(int limit = 20)
    {
        var rows = new List<PlaytimeByGameRow>();
        if (_connection is null)
        {
            return rows;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT s.game_id,
                       COALESCE(g.title, s.game_id) AS title,
                       SUM(s.duration_seconds)      AS total_seconds,
                       COUNT(*)                     AS sessions
                FROM play_sessions s
                LEFT JOIN games g ON g.id = s.game_id
                GROUP BY s.game_id
                ORDER BY total_seconds DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new PlaytimeByGameRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetInt64(3)));
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading playtime totals failed: {exception.Message}";
        }
        return rows;
    }

    /// <summary>Most recent sessions across every game, newest first (Stats page).</summary>
    public IReadOnlyList<PlaySessionRecord> LoadRecentPlaySessions(int limit = 25)
    {
        var sessions = new List<PlaySessionRecord>();
        if (_connection is null)
        {
            return sessions;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, game_id, started_at, ended_at, duration_seconds
                FROM play_sessions
                ORDER BY started_at DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new PlaySessionRecord
                {
                    Id = reader.GetInt64(0),
                    GameId = reader.GetString(1),
                    StartedAt = reader.GetDateTime(2),
                    EndedAt = reader.GetDateTime(3),
                    DurationSeconds = reader.GetDouble(4),
                });
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading recent sessions failed: {exception.Message}";
        }
        return sessions;
    }

    /// <summary>Grand playtime totals (Stats page summary cards).</summary>
    public (double TotalSeconds, long Sessions, double WeekSeconds, double MonthSeconds) LoadPlaytimeTotals()
    {
        const double noData = 0;
        if (_connection is null)
        {
            return (noData, 0, noData, noData);
        }
        try
        {
            var weekStart = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek).Date;
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(SUM(duration_seconds), 0),
                       COUNT(*),
                       COALESCE(SUM(CASE WHEN started_at >= $weekStart THEN duration_seconds ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN started_at >= $monthStart THEN duration_seconds ELSE 0 END), 0)
                FROM play_sessions
                """;
            command.Parameters.AddWithValue("$weekStart", weekStart.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$monthStart", monthStart.ToString("O", CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetDouble(0), reader.GetInt64(1), reader.GetDouble(2), reader.GetDouble(3));
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading playtime totals failed: {exception.Message}";
        }
        return (noData, 0, noData, noData);
    }

    private static PokemonGame MapGame(SqliteDataReader reader)
    {
        DateTime? lastPlayed = null;
        if (!reader.IsDBNull(10) && DateTime.TryParse(reader.GetString(10), out var parsed))
        {
            lastPlayed = parsed;
        }
        return new PokemonGame
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Generation = reader.GetInt32(2),
            Platform = reader.GetString(3),
            ReleaseYear = reader.GetInt32(4),
            Description = reader.GetString(5),
            CoverFromHex = reader.GetString(6),
            CoverToHex = reader.GetString(7),
            IsFavorite = reader.GetInt32(8) != 0,
            RomPath = reader.IsDBNull(9) ? null : reader.GetString(9),
            LastPlayed = lastPlayed,
            PlaytimeHours = reader.GetDouble(11),
            IsImported = reader.GetInt32(12) != 0,
            ImportedAt = reader.IsDBNull(13)
                ? null
                : DateTime.TryParse(reader.GetString(13), out var importedAt) ? importedAt : null,
        };
    }

    private static void FillGameParameters(SqliteCommand command, PokemonGame game)
    {
        command.Parameters.AddWithValue("$id", game.Id);
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$generation", game.Generation);
        command.Parameters.AddWithValue("$platform", game.Platform);
        command.Parameters.AddWithValue("$releaseYear", game.ReleaseYear);
        command.Parameters.AddWithValue("$description", game.Description);
        command.Parameters.AddWithValue("$coverFrom", game.CoverFromHex);
        command.Parameters.AddWithValue("$coverTo", game.CoverToHex);
        command.Parameters.AddWithValue("$romPath", (object?)game.RomPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$importedAt",
            (object?)game.ImportedAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
    }

    private void UpsertGame(PokemonGame game, SqliteTransaction transaction)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO games (id, title, generation, platform, release_year, description, cover_from, cover_to)
            VALUES ($id, $title, $generation, $platform, $releaseYear, $description, $coverFrom, $coverTo)
            ON CONFLICT (id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", game.Id);
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$generation", game.Generation);
        command.Parameters.AddWithValue("$platform", game.Platform);
        command.Parameters.AddWithValue("$releaseYear", game.ReleaseYear);
        command.Parameters.AddWithValue("$description", game.Description);
        command.Parameters.AddWithValue("$coverFrom", game.CoverFromHex);
        command.Parameters.AddWithValue("$coverTo", game.CoverToHex);
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ hack bookmarks

    /// <summary>Loads active hack bookmarks as hack-id → true. Empty when none or on failure.</summary>
    public IReadOnlyDictionary<string, bool> LoadHackBookmarks()
    {
        try
        {
            var bookmarks = new Dictionary<string, bool>();
            if (_connection is null)
            {
                return bookmarks;
            }
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT hack_id FROM hack_bookmarks WHERE bookmarked = 1";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                bookmarks[reader.GetString(0)] = true;
            }
            return bookmarks;
        }
        catch (Exception exception)
        {
            LoadError = $"Reading bookmarks failed: {exception.Message}";
            return new Dictionary<string, bool>();
        }
    }

    public void SaveHackBookmark(string hackId, bool bookmarked)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO hack_bookmarks (hack_id, bookmarked) VALUES ($id, $value)
                ON CONFLICT (hack_id) DO UPDATE SET bookmarked = $value
                """;
            command.Parameters.AddWithValue("$id", hackId);
            command.Parameters.AddWithValue("$value", bookmarked ? 1 : 0);
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Saving bookmark failed: {exception.Message}";
        }
    }

        // ------------------------------------------------------------------ rom hacks

    /// <summary>All user-created ROM hacks, newest first. Empty list when none exist —
    /// the browser is user-managed, so an empty library is honest (no fictional seeds).</summary>
    public IReadOnlyList<RomHack> LoadRomHacks()
    {
        var hacks = new List<RomHack>();
        if (_connection is null)
        {
            return hacks;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, base_game_id, description, creator, url, rom_path,
                       genre, difficulty, status, created_at
                FROM rom_hacks
                ORDER BY created_at DESC, title COLLATE NOCASE
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime? createdAt = reader.IsDBNull(10)
                    ? null
                    : DateTime.TryParse(reader.GetString(10), out var parsed) ? parsed : null;
                hacks.Add(new RomHack
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    BaseGameId = reader.GetString(2),
                    Description = reader.GetString(3),
                    Creator = reader.GetString(4),
                    ExternalUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
                    RomPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Genre = reader.GetString(7),
                    Difficulty = reader.GetString(8),
                    Status = reader.GetString(9),
                    CreatedAt = createdAt,
                });
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading ROM hacks failed: {exception.Message}";
        }
        return hacks;
    }

    /// <summary>Inserts or updates a user-created ROM hack.</summary>
    public bool SaveRomHack(RomHack hack)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO rom_hacks
                    (id, title, base_game_id, description, creator, url, rom_path,
                     genre, difficulty, status, created_at)
                VALUES
                    ($id, $title, $baseGameId, $description, $creator, $url, $romPath,
                     $genre, $difficulty, $status, $createdAt)
                ON CONFLICT (id) DO UPDATE SET
                    title = $title, base_game_id = $baseGameId, description = $description,
                    creator = $creator, url = $url, rom_path = $romPath,
                    genre = $genre, difficulty = $difficulty, status = $status
                """;
            command.Parameters.AddWithValue("$id", hack.Id);
            command.Parameters.AddWithValue("$title", hack.Title);
            command.Parameters.AddWithValue("$baseGameId", hack.BaseGameId);
            command.Parameters.AddWithValue("$description", hack.Description ?? string.Empty);
            command.Parameters.AddWithValue("$creator", hack.Creator ?? string.Empty);
            command.Parameters.AddWithValue("$url", hack.ExternalUrl ?? string.Empty);
            command.Parameters.AddWithValue("$romPath", (object?)hack.RomPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$genre", hack.Genre);
            command.Parameters.AddWithValue("$difficulty", hack.Difficulty);
            command.Parameters.AddWithValue("$status", hack.Status);
            command.Parameters.AddWithValue("$createdAt",
                (object?)hack.CreatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            LoadError = $"Saving ROM hack failed: {exception.Message}";
            return false;
        }
    }

    /// <summary>Deletes a ROM hack. The bookmark and the managed file (if any) are
    /// cleaned up by the caller.</summary>
    public bool DeleteRomHack(string hackId)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM rom_hacks WHERE id = $id";
            command.Parameters.AddWithValue("$id", hackId);
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception exception)
        {
            LoadError = $"Deleting ROM hack failed: {exception.Message}";
            return false;
        }
    }

    // ------------------------------------------------------------------ collections

    /// <summary>
    /// Loads collections plus their member game ids. In persistent mode this returns
    /// exactly what the user has — an empty library state is honored (a deleted
    /// collection stays deleted); the seed catalog is only a fallback for degraded
    /// (no-database) mode.
    /// </summary>
    public IReadOnlyList<GameCollection> LoadCollections()
    {
        if (_connection is null)
        {
            return MockCatalog.CreateCollections();
        }

        try
        {
            var definitions = new Dictionary<string, (string Name, string Description)>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, description FROM collections ORDER BY name COLLATE NOCASE";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    definitions[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
                }
            }

            var gameIds = new Dictionary<string, List<string>>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT collection_id, game_id FROM collection_games";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (!gameIds.TryGetValue(key, out var list))
                    {
                        list = new List<string>();
                        gameIds[key] = list;
                    }
                    list.Add(reader.GetString(1));
                }
            }

            var result = new List<GameCollection>();
            foreach (var (id, definition) in definitions)
            {
                result.Add(new GameCollection
                {
                    Id = id,
                    Name = definition.Name,
                    Description = definition.Description,
                    GameIds = gameIds.TryGetValue(id, out var ids) ? ids : new List<string>(),
                });
            }
            return result;
        }
        catch (Exception exception)
        {
            LoadError = $"Reading collections failed: {exception.Message}";
            return new List<GameCollection>();
        }
    }

    /// <summary>Inserts a new user collection with its initial members. Returns false on failure.</summary>
    public bool AddCollection(GameCollection collection)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var transaction = _connection.BeginTransaction();
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO collections (id, name, description) VALUES ($id, $name, $description)
                    """;
                command.Parameters.AddWithValue("$id", collection.Id);
                command.Parameters.AddWithValue("$name", collection.Name);
                command.Parameters.AddWithValue("$description", collection.Description);
                command.ExecuteNonQuery();
            }
            InsertCollectionLinks(collection, transaction);
            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            LoadError = $"Adding collection failed: {exception.Message}";
            return false;
        }
    }

    /// <summary>Updates a collection's name/description and replaces its member list.</summary>
    public bool SaveCollection(GameCollection collection)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var transaction = _connection.BeginTransaction();
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE collections SET name = $name, description = $description WHERE id = $id
                    """;
                command.Parameters.AddWithValue("$name", collection.Name);
                command.Parameters.AddWithValue("$description", collection.Description);
                command.Parameters.AddWithValue("$id", collection.Id);
                command.ExecuteNonQuery();
            }
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM collection_games WHERE collection_id = $id";
                command.Parameters.AddWithValue("$id", collection.Id);
                command.ExecuteNonQuery();
            }
            InsertCollectionLinks(collection, transaction);
            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            LoadError = $"Saving collection failed: {exception.Message}";
            return false;
        }
    }

    /// <summary>Deletes a collection; member links cascade. Games in the library are untouched.</summary>
    public bool DeleteCollection(string collectionId)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM collections WHERE id = $id";
            command.Parameters.AddWithValue("$id", collectionId);
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception exception)
        {
            LoadError = $"Deleting collection failed: {exception.Message}";
            return false;
        }
    }

    private void InsertCollectionLinks(GameCollection collection, SqliteTransaction transaction)
    {
        foreach (var gameId in collection.GameIds)
        {
            using var command = _connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO collection_games (collection_id, game_id) VALUES ($collectionId, $gameId)
                ON CONFLICT (collection_id, game_id) DO NOTHING
                """;
            command.Parameters.AddWithValue("$collectionId", collection.Id);
            command.Parameters.AddWithValue("$gameId", gameId);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Toggles one game's membership in one collection (details-page chips).</summary>
    public bool SetGameMembership(string gameId, string collectionId, bool member)
    {
        if (_connection is null)
        {
            return false;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = member
                ? "INSERT INTO collection_games (collection_id, game_id) VALUES ($cid, $gid) ON CONFLICT (collection_id, game_id) DO NOTHING"
                : "DELETE FROM collection_games WHERE collection_id = $cid AND game_id = $gid";
            command.Parameters.AddWithValue("$cid", collectionId);
            command.Parameters.AddWithValue("$gid", gameId);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            LoadError = $"Updating collection membership failed: {exception.Message}";
            return false;
        }
    }

    /// <summary>Names of the collections that contain a game, for the details page.</summary>
    public IReadOnlyList<string> LoadCollectionNamesForGame(string gameId)
    {
        var names = new List<string>();
        if (_connection is null)
        {
            return names;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT c.name FROM collections c
                JOIN collection_games cg ON cg.collection_id = c.id
                WHERE cg.game_id = $gameId
                ORDER BY c.name COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("$gameId", gameId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading game collections failed: {exception.Message}";
        }
        return names;
    }

    private void UpsertCollection(GameCollection collection, SqliteTransaction transaction)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO collections (id, name, description) VALUES ($id, $name, $description)
            ON CONFLICT (id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", collection.Id);
        command.Parameters.AddWithValue("$name", collection.Name);
        command.Parameters.AddWithValue("$description", collection.Description);
        command.ExecuteNonQuery();

        foreach (var gameId in collection.GameIds)
        {
            using var link = _connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO collection_games (collection_id, game_id) VALUES ($collectionId, $gameId)
                ON CONFLICT (collection_id, game_id) DO NOTHING
                """;
            link.Parameters.AddWithValue("$collectionId", collection.Id);
            link.Parameters.AddWithValue("$gameId", gameId);
            link.ExecuteNonQuery();
        }
    }

    // ------------------------------------------------------------------ emulator configs

    /// <summary>Loads emulator configurations keyed by system key. Empty on failure.</summary>
    public IReadOnlyDictionary<string, EmulatorConfig> LoadEmulatorConfigs()
    {
        var configs = new Dictionary<string, EmulatorConfig>();
        if (_connection is null)
        {
            return configs;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT system_key, executable_path, arguments FROM emulator_configs";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                configs[key] = new EmulatorConfig
                {
                    SystemKey = key,
                    ExecutablePath = reader.GetString(1),
                    Arguments = reader.GetString(2),
                };
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading emulator configs failed: {exception.Message}";
        }
        return configs;
    }

    public void SaveEmulatorConfig(string systemKey, string executablePath, string arguments)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            EnsureEmulatorRow(systemKey);
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE emulator_configs
                SET executable_path = $path, arguments = $args
                WHERE system_key = $key
                """;
            command.Parameters.AddWithValue("$path", executablePath ?? string.Empty);
            command.Parameters.AddWithValue("$args", arguments ?? string.Empty);
            command.Parameters.AddWithValue("$key", systemKey);
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Saving emulator config failed: {exception.Message}";
        }
    }

    private void EnsureEmulatorRow(string systemKey)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = """
            INSERT INTO emulator_configs (system_key, executable_path, arguments)
            VALUES ($key, '', '')
            ON CONFLICT (system_key) DO NOTHING
            """;
        command.Parameters.AddWithValue("$key", systemKey);
        command.ExecuteNonQuery();
    }

    private void UpsertEmulatorConfigCore(string systemKey, string executablePath, string arguments, SqliteTransaction transaction)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO emulator_configs (system_key, executable_path, arguments)
            VALUES ($key, $path, $args)
            ON CONFLICT (system_key) DO NOTHING
            """;
        command.Parameters.AddWithValue("$key", systemKey);
        command.Parameters.AddWithValue("$path", executablePath);
        command.Parameters.AddWithValue("$args", arguments);
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ emulator downloads

    /// <summary>Loads recorded emulator installs keyed by manifest emulator id. Empty on failure.</summary>
    public IReadOnlyDictionary<string, EmulatorDownloadRecord> LoadEmulatorDownloads()
    {
        var records = new Dictionary<string, EmulatorDownloadRecord>(StringComparer.OrdinalIgnoreCase);
        if (_connection is null)
        {
            return records;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT emulator_id, version, sha256, source_url, install_folder, executable_path, downloaded_at
                FROM emulator_downloads
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!DateTime.TryParse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var downloadedAt))
                {
                    downloadedAt = DateTime.MinValue;
                }
                var id = reader.GetString(0);
                records[id] = new EmulatorDownloadRecord
                {
                    EmulatorId = id,
                    Version = reader.GetString(1),
                    Sha256 = reader.GetString(2),
                    SourceUrl = reader.GetString(3),
                    InstallFolder = reader.GetString(4),
                    ExecutablePath = reader.GetString(5),
                    DownloadedAt = downloadedAt,
                };
            }
        }
        catch (Exception exception)
        {
            LoadError = $"Reading emulator downloads failed: {exception.Message}";
        }
        return records;
    }

    public void SaveEmulatorDownload(EmulatorDownloadRecord record)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO emulator_downloads
                    (emulator_id, version, sha256, source_url, install_folder, executable_path, downloaded_at)
                VALUES ($id, $version, $sha256, $sourceUrl, $installFolder, $executablePath, $downloadedAt)
                ON CONFLICT (emulator_id) DO UPDATE SET
                    version = $version,
                    sha256 = $sha256,
                    source_url = $sourceUrl,
                    install_folder = $installFolder,
                    executable_path = $executablePath,
                    downloaded_at = $downloadedAt
                """;
            command.Parameters.AddWithValue("$id", record.EmulatorId);
            command.Parameters.AddWithValue("$version", record.Version);
            command.Parameters.AddWithValue("$sha256", record.Sha256);
            command.Parameters.AddWithValue("$sourceUrl", record.SourceUrl);
            command.Parameters.AddWithValue("$installFolder", record.InstallFolder);
            command.Parameters.AddWithValue("$executablePath", record.ExecutablePath);
            command.Parameters.AddWithValue("$downloadedAt", record.DownloadedAt.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Saving emulator download failed: {exception.Message}";
        }
    }

    public void DeleteEmulatorDownload(string emulatorId)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM emulator_downloads WHERE emulator_id = $id";
            command.Parameters.AddWithValue("$id", emulatorId);
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Deleting emulator download failed: {exception.Message}";
        }
    }

    // ------------------------------------------------------------------ settings

    public string? GetSetting(string key)
    {
        if (_connection is null)
        {
            return null;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
        catch (Exception exception)
        {
            LoadError = $"Reading setting '{key}' failed: {exception.Message}";
            return null;
        }
    }

    public void SetSetting(string key, string value)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT (key) DO UPDATE SET value = $value
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Saving setting '{key}' failed: {exception.Message}";
        }
    }

    // ------------------------------------------------------------------ helpers

    private long Scalar(string sql)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private void Execute(string sql)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void ExecuteNonQuery(string sql, params (string Name, object Value)[] parameters)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            LoadError = $"Save failed: {exception.Message}";
        }
    }
}
