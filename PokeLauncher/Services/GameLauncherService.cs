using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PokeLauncher.Models;

namespace PokeLauncher.Services;

/// <summary>Result of one launch attempt; drives both toasts and inline error UI.</summary>
public sealed class LaunchAttempt
{
    public static LaunchAttempt Ok(GameLaunchSession session) =>
        new() { Success = true, Session = session };

    public static LaunchAttempt Fail(string error, bool missingRom = false, bool missingEmulator = false) =>
        new() { Success = false, Error = error, MissingRom = missingRom, MissingEmulator = missingEmulator };

    public bool Success { get; private init; }

    /// <summary>User-facing explanation when <see cref="Success"/> is false.</summary>
    public string? Error { get; private init; }

    /// <summary>True when the problem is the ROM file (missing/never imported) — the UI can offer importing.</summary>
    public bool MissingRom { get; private init; }

    /// <summary>True when the problem is the emulator configuration — the UI can offer opening Settings.</summary>
    public bool MissingEmulator { get; private init; }

    public GameLaunchSession? Session { get; private init; }
}

/// <summary>How one emulator session ended. Raised on a background thread.</summary>
public sealed class GameSessionEnd
{
    public required DateTime StartedAt { get; init; }

    public required DateTime EndedAt { get; init; }

    public required int ExitCode { get; init; }

    public required double DurationSeconds { get; init; }

    public bool ExitedCleanly => ExitCode == 0;

    /// <summary>The emulator process was gone almost immediately — usually a bad configuration.</summary>
    public bool ExitedWithinSeconds => DurationSeconds < 3.0;
}

/// <summary>
/// One running emulator instance for one game. The launcher raises <see cref="Ended"/>
/// when the process exits; PokeLauncher records the play session and updates totals then.
/// </summary>
public sealed class GameLaunchSession
{
    public required string GameId { get; init; }

    public required string GameTitle { get; init; }

    public required string SystemKey { get; init; }

    public required DateTime StartedAt { get; init; }

    public required Process Process { get; init; }

    /// <summary>Raised on a background thread when the emulator process exits.</summary>
    public event Action<GameLaunchSession, GameSessionEnd>? Ended;

    internal void RaiseEnded(GameSessionEnd end)
    {
        try
        {
            Ended?.Invoke(this, end);
        }
        catch
        {
            /* a listener failure must never crash the app's background thread */
        }
    }
}

/// <summary>
/// Launches games through the user's configured emulators and reports when they exit so
/// playtime can be tracked. Emulator configuration is read fresh from the database on
/// every launch, so changes made in Settings apply immediately without a restart.
/// Process starts are non-blocking; process exit is observed via events, never by waiting.
/// </summary>
public sealed class GameLauncherService
{
    private readonly AppDatabase _database;
    private readonly HashSet<string> _runningGameIds = new(StringComparer.Ordinal);
    private readonly object _runningGate = new();

    public GameLauncherService(AppDatabase database)
    {
        _database = database;
    }

    /// <summary>Raised on the UI thread right after an emulator process started.</summary>
    public event Action<GameLaunchSession>? SessionStarted;

    /// <summary>Raised on a background thread when an emulator process exits.</summary>
    public event Action<GameLaunchSession, GameSessionEnd>? SessionEnded;

    /// <summary>Whether a launch for this game is currently running (per app session).</summary>
    public bool IsGameRunning(string gameId)
    {
        lock (_runningGate)
        {
            return _runningGameIds.Contains(gameId);
        }
    }

    /// <summary>
    /// Validates everything needed to launch and starts the emulator process. Throws
    /// nothing for expected failures — problems come back as descriptive results.
    /// </summary>
    public LaunchAttempt Launch(PokemonGame game)
    {
        if (IsGameRunning(game.Id))
        {
            return LaunchAttempt.Fail($"{game.Title} is already running.");
        }

        // ---- ROM checks (a failed launch must never create a play session)
        if (string.IsNullOrWhiteSpace(game.RomPath))
        {
            return LaunchAttempt.Fail(
                $"{game.Title} has no ROM file yet. Import one first — either with the Import button on this page or from My Library.",
                missingRom: true);
        }
        if (!File.Exists(game.RomPath))
        {
            return LaunchAttempt.Fail(
                $"The ROM file for {game.Title} can't be found: {game.RomPath}. " +
                "It may have been moved, renamed, or deleted. Re-import it or fix the path.",
                missingRom: true);
        }

        // ---- Emulator checks against the live configuration
        var systemKey = EmulatorSystems.SystemKeyForPlatform(game.Platform);
        if (systemKey is null)
        {
            return LaunchAttempt.Fail(
                $"“{game.Platform}” isn't one of PokeLauncher's supported systems yet.");
        }

        var configs = _database.LoadEmulatorConfigs();
        if (!configs.TryGetValue(systemKey, out var config) || string.IsNullOrWhiteSpace(config.ExecutablePath))
        {
            return LaunchAttempt.Fail(
                $"No {game.Platform} emulator is configured. Open Settings → Emulators to point one at your own copy, " +
                "or download an official build straight from its developers.",
                missingEmulator: true);
        }
        if (!File.Exists(config.ExecutablePath))
        {
            return LaunchAttempt.Fail(
                $"The configured {game.Platform} emulator wasn't found: {config.ExecutablePath}. " +
                "It may have been moved or deleted — open Settings to fix the path or re-download the official build.",
                missingEmulator: true);
        }

        // ---- Start the process
        var startedAt = DateTime.Now;
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = config.ExecutablePath,
                Arguments = BuildArguments(config.Arguments, game.RomPath!),
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(config.ExecutablePath) ?? string.Empty,
            });
        }
        catch (Exception exception)
        {
            return LaunchAttempt.Fail(
                $"Failed to start the {game.Platform} emulator: {exception.Message}");
        }
        if (process is null)
        {
            return LaunchAttempt.Fail(
                $"The {game.Platform} emulator could not be started (the system returned no process).");
        }

        var session = new GameLaunchSession
        {
            GameId = game.Id,
            GameTitle = game.Title,
            SystemKey = systemKey,
            StartedAt = startedAt,
            Process = process,
        };
        session.Ended += (_, end) => OnSessionProcessExited(session, end);

        // Observe the emulator process. Exited fires on a thread-pool thread; the guard
        // makes sure the session end is reported exactly once even if the process already
        // exited before the hook was attached.
        var endRaised = 0;
        void FireSessionEnd()
        {
            if (Interlocked.Exchange(ref endRaised, 1) != 0)
            {
                return;
            }
            var endedAt = DateTime.Now;
            var exitCode = 0;
            try
            {
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                }
            }
            catch
            {
                /* exit code unavailable (e.g. crashed handle); report a clean-ish default */
            }
            session.RaiseEnded(new GameSessionEnd
            {
                StartedAt = startedAt,
                EndedAt = endedAt,
                ExitCode = exitCode,
                DurationSeconds = (endedAt - startedAt).TotalSeconds,
            });
        }
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => FireSessionEnd();
            if (process.HasExited)
            {
                // Exited was missed (process finished before the hook); report now.
                FireSessionEnd();
            }
        }
        catch
        {
            /* extremely restricted environments can refuse; the session simply won't be tracked */
        }

        lock (_runningGate)
        {
            _runningGameIds.Add(game.Id);
        }
        SessionStarted?.Invoke(session);
        return LaunchAttempt.Ok(session);
    }

    private void OnSessionProcessExited(GameLaunchSession session, GameSessionEnd end)
    {
        lock (_runningGate)
        {
            _runningGameIds.Remove(session.GameId);
        }
        SessionEnded?.Invoke(session, end);
    }

    /// <summary>
    /// Substitutes the ROM path into an emulator's argument template. <c>{rom}</c> becomes
    /// the quoted absolute path; a template without the placeholder gets the quoted path
    /// appended, so hand-configured emulators still receive the game.
    /// </summary>
    public static string BuildArguments(string? template, string romPath)
    {
        var quoted = $"\"{romPath}\"";
        if (string.IsNullOrWhiteSpace(template))
        {
            return quoted;
        }
        var replaced = template.Replace("{rom}", quoted, StringComparison.OrdinalIgnoreCase);
        return replaced.Contains(quoted, StringComparison.Ordinal)
            ? replaced
            : $"{replaced} {quoted}";
    }
}
