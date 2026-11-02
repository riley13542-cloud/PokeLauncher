using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PokeLauncher.Services;

/// <summary>
/// Discord Rich Presence over Discord's local IPC pipe (no server, no accounts).
/// Requires the Discord desktop app to be running; without it the service stays
/// idle. Every operation is best-effort: Discord not running, the pipe missing, or
/// any protocol hiccup never affects the launcher.
/// </summary>
public sealed class DiscordPresenceService : IDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpPing = 3;

    /// <summary>
    /// PokeLauncher's Discord application. Presence works out of the box with no
    /// user setup; uploading artwork named "launcher" to that application page on
    /// discord.com/developers adds the cover image automatically.
    /// </summary>
    public const string DefaultClientId = "1566616918691946586";

    private readonly Func<string?> _resolveClientId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private NamedPipeClientStream? _pipe;
    private (string Details, string State)? _presence;
    private long _startedAtUnix;
    private bool _dirty;

    /// <summary>Last connection outcome, for diagnostics ("connected", "discord not running", ...).</summary>
    public string LastStatus { get; private set; } = "idle";

    public DiscordPresenceService(Func<string?> resolveClientId)
    {
        _resolveClientId = resolveClientId;
    }

    /// <summary>Shows "playing <paramref name="title"/>" until cleared or replaced.</summary>
    public void SetPresence(string title, string platform)
    {
        var clientId = _resolveClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }
        _presence = (Truncate(title, 120), Truncate(platform, 120));
        _startedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _dirty = true;
        EnsureConnected();
    }

    /// <summary>Clears the presence (emulator closed).</summary>
    public void Clear()
    {
        if (_pipe is null || !_pipe.IsConnected)
        {
            _presence = null;
            return;
        }
        _presence = null;
        _dirty = true;
    }

    public void Shutdown()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { /* shutting down anyway */ }
    }

    public void Dispose() => Shutdown();

    // ------------------------------------------------------------- connection

    private void EnsureConnected()
    {
        if (_pipe is { IsConnected: true })
        {
            return;
        }
        var clientId = _resolveClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }
        if (_pipe is not null && _cts is not null)
        {
            // Stale pipe from a previous Discord session - restart the pump.
            _cts.Cancel();
        }

        _cts = new CancellationTokenSource();
        var pipe = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut);
        _pipe = pipe;
        var token = _cts.Token;
        var id = clientId;
        _ = Task.Run(async () => await RunPumpAsync(pipe, id, token), token);
    }

    private async Task RunPumpAsync(NamedPipeClientStream pipe, string clientId, CancellationToken token)
    {
        try
        {
            await pipe.ConnectAsync(3000, token);

            await WriteFrameAsync(pipe, OpHandshake,
                JsonSerializer.Serialize(new { v = 1, client_id = clientId }), token);

            // A dedicated reader parses Discord's frames and signals when the
            // handshake round-trip completes (the READY event). Blocking reads are
            // fine here because cancelling the token disposes the pipe, which
            // unblocks (and ends) the read with an exception.
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (!TryReadFrame(pipe, out var payload))
                        {
                            break;
                        }
                        var text = Encoding.UTF8.GetString(payload);
                        if (text.Contains("READY", StringComparison.OrdinalIgnoreCase))
                        {
                            ready.TrySetResult(true);
                        }
                    }
                }
                catch { /* pipe closed - expected on shutdown */ }
            }, token);

            LastStatus = "connected; waiting for READY";

            // Wait for READY (with a deadline) before pushing presence.
            var readyInTime = await Task.WhenAny(ready.Task, Task.Delay(5000, token)) == ready.Task;
            LastStatus = readyInTime ? "connected" : "connected (no READY yet)";

            if (readyInTime)
            {
                _dirty = true;
            }

            var lastBeat = DateTime.UtcNow;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                if (_dirty)
                {
                    _dirty = false;
                    await SendPresenceAsync(pipe, token);
                }

                if (DateTime.UtcNow - lastBeat >= TimeSpan.FromSeconds(15))
                {
                    lastBeat = DateTime.UtcNow;
                    await WriteFrameAsync(pipe, OpPing, JsonSerializer.Serialize(new { nonce = Guid.NewGuid().ToString() }), token);
                }

                await timer.WaitForNextTickAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            TryClearPresence(pipe);
            pipe.Dispose();
            if (ReferenceEquals(_pipe, pipe))
            {
                _pipe = null;
            }
        }
        catch (Exception exception)
        {
            // Discord not running / user closed it / protocol change: silent by design,
            // but the status keeps a breadcrumb for the Settings page / diagnostics.
            LastStatus = exception.Message.Length > 80 ? exception.Message[..80] : exception.Message;
            TryClearPresence(pipe);
            pipe.Dispose();
            if (ReferenceEquals(_pipe, pipe))
            {
                _pipe = null;
            }
        }
    }

    private async Task SendPresenceAsync(NamedPipeClientStream pipe, CancellationToken token)
    {
        object activity = _presence is { } presence
            ? new
            {
                details = presence.Details,
                state = presence.State,
                // Discord expects Unix epoch seconds (an integer), not a date string.
                timestamps = new { start = _startedAtUnix },
                assets = new { large_image = "launcher", large_text = "PokeLauncher" },
                instance = false,
            }
            : (object)new { };

        var payload = JsonSerializer.Serialize(new
        {
            cmd = "SET_ACTIVITY",
            args = new
            {
                pid = Process.GetCurrentProcess().Id,
                activity,
            },
            nonce = Guid.NewGuid().ToString(),
        });
        await WriteFrameAsync(pipe, OpFrame, payload, token);
    }

    private static void TryClearPresence(NamedPipeClientStream pipe)
    {
        try
        {
            if (pipe.IsConnected)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    cmd = "SET_ACTIVITY",
                    args = new { pid = Process.GetCurrentProcess().Id, activity = new { } },
                    nonce = Guid.NewGuid().ToString(),
                });
                WriteFrame(pipe, OpFrame, payload);
            }
        }
        catch
        {
            /* best effort */
        }
    }

    // ------------------------------------------------------------- framing

    private static async Task WriteFrameAsync(NamedPipeClientStream pipe, int op, string json, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await pipe.WriteAsync(frame, token);
        await pipe.FlushAsync(token);
    }

    private static void WriteFrame(NamedPipeClientStream pipe, int op, string json)
    {
        WriteFrameAsync(pipe, op, json, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static bool TryReadFrame(NamedPipeClientStream pipe, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        try
        {
            var header = new byte[8];
            if (!ReadExactly(pipe, header))
            {
                return false;
            }
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (length is < 0 or > 512 * 1024)
            {
                return false;
            }
            payload = new byte[length];
            return ReadExactly(pipe, payload);
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadExactly(NamedPipeClientStream pipe, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = pipe.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }

    private static string Truncate(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
