using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Adapters;

/// <summary>
/// Base class for adapters that receive events over a persistent outbound WebSocket.
/// Subclasses implement platform-specific session logic (auth, ACK, heartbeat, resume).
/// The reconnect loop, backoff math, and ClientWebSocket lifecycle are handled here.
/// </summary>
public abstract class AdapterWebSocketService : BackgroundService
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ILogger _logger;

    protected AdapterWebSocketService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called on every connection attempt (initial and reconnect).
    /// Slack: calls apps.connections.open each time (one-time URLs).
    /// Discord: calls GET /gateway/bot once, caches for 7 days.
    /// </summary>
    protected abstract Task<Uri> GetConnectionUriAsync(CancellationToken ct);

    /// <summary>
    /// Called after the WebSocket is connected. Runs the full session:
    /// authenticate, receive loop, heartbeat (if needed), etc.
    /// Returns when the session ends (gracefully or on error).
    /// The base class catches exceptions and reconnects.
    /// </summary>
    protected abstract Task RunSessionAsync(WebSocket ws, CancellationToken ct);

    /// <summary>
    /// Returns the delay before the next reconnect attempt, or null to stop retrying.
    /// Default: 2s → 30s exponential backoff ×1.8, ±25% jitter, max 12 attempts.
    /// </summary>
    protected virtual TimeSpan? GetReconnectDelay(int attempt) => DefaultBackoff(attempt);

    /// <summary>
    /// Whether a given exception should trigger reconnect or give up immediately.
    /// Default: reconnect on all exceptions except OperationCanceledException.
    /// Discord overrides this to not reconnect on INVALID_SESSION without clearing session state.
    /// </summary>
    protected virtual bool ShouldReconnect(Exception ex) => ex is not OperationCanceledException;

    /// <summary>
    /// Connects the given <paramref name="ws"/> to <paramref name="uri"/>.
    /// Override in tests to avoid real network I/O.
    /// </summary>
    protected virtual Task ConnectAsync(ClientWebSocket ws, Uri uri, CancellationToken ct)
        => ws.ConnectAsync(uri, ct);

    /// <summary>
    /// Sends a text frame over the WebSocket. Subclasses call this for ACKs,
    /// heartbeats, IDENTIFY, RESUME, etc. Handles WebSocketMessageType internally
    /// so subclasses never touch it directly.
    ///
    /// Thread-safety: wraps a SemaphoreSlim(1,1) to serialise concurrent sends.
    /// ClientWebSocket throws InvalidOperationException if two SendAsync calls overlap
    /// on the same instance. Subclasses must always send through this method — never
    /// call ws.SendAsync directly — to guarantee at-most-one send in flight.
    /// This matters for Slack where ACK and any proactive send could race.
    /// </summary>
    protected async Task SendAsync(WebSocket ws, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    protected override sealed async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            ClientWebSocket? ws = null;
            try
            {
                var uri = await GetConnectionUriAsync(stoppingToken);
                ws = new ClientWebSocket();
                await ConnectAsync(ws, uri, stoppingToken);
                // Reset on successful connect, not on successful session end.
                // If RunSessionAsync throws immediately (e.g. auth failure during HELLO),
                // the backoff still starts at index 0 because the TCP/WS connection itself
                // succeeded. This is intentional: "attempt" counts consecutive connection
                // failures, not consecutive session failures.
                attempt = 0;
                await RunSessionAsync(ws, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ShouldReconnect(ex))
            {
                var delay = GetReconnectDelay(attempt);
                if (delay is null)
                {
                    _logger.LogError(ex, "WebSocket adapter reached max reconnect attempts ({Attempt}). Giving up.", attempt);
                    return;
                }
                _logger.LogWarning(ex, "WebSocket adapter disconnected. Reconnecting in {Delay}ms (attempt {Attempt}).", (int)delay.Value.TotalMilliseconds, attempt + 1);
                await Task.Delay(delay.Value, stoppingToken);
                attempt++;
            }
            finally
            {
                ws?.Dispose();
            }
        }
    }

    // 2s → 30s, ×1.8, ±25% jitter, max 12 attempts — matches OpenClaw reconnect-policy.ts
    private static TimeSpan? DefaultBackoff(int attempt)
    {
        if (attempt >= 12) return null;
        var baseMs = 2_000 * Math.Pow(1.8, attempt);
        var capped  = Math.Min(baseMs, 30_000);
        var jitter  = 0.75 + Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromMilliseconds(capped * jitter);
    }
}
