using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Adapters.Slack.SocketMode;

/// <summary>
/// BackgroundService that maintains a persistent outbound WebSocket connection to
/// Slack's Socket Mode gateway. Registered automatically when
/// <see cref="SlackAdapterConfig.AppToken"/> is non-null.
/// </summary>
public sealed class SlackSocketModeService(
    SlackSocketModeClient client,
    SlackAdapter adapter,
    ILogger<SlackSocketModeService> logger)
    : AdapterWebSocketService(logger)
{
    // Buffer size for receiving WebSocket frames.
    // Slack Socket Mode envelopes are typically <16KB; 64KB handles edge cases.
    private const int ReceiveBufferSize = 64 * 1024;

    /// <inheritdoc/>
    /// Slack one-time URLs expire immediately after use — always fetch a fresh one.
    protected override Task<Uri> GetConnectionUriAsync(CancellationToken ct)
        => client.OpenConnectionUrlAsync(ct);

    /// <inheritdoc/>
    /// Receive loop: read frame → ACK via SendAsync (held by base send lock) →
    /// dispatch to adapter. Exits when the WebSocket closes or ct is cancelled.
    protected override async Task RunSessionAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[ReceiveBufferSize];
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            sb.Clear();
            WebSocketReceiveResult result;

            // Read potentially multi-frame message
            do
            {
                var segment = new ArraySegment<byte>(buffer);
                result = await ws.ReceiveAsync(segment, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            var json = sb.ToString();

            SlackSocketEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<SlackSocketEnvelope>(
                    json, SlackAdapterJsonContext.Default.Options);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to deserialize Slack Socket Mode envelope. Raw: {Json}", json);
                continue;
            }

            if (envelope is null) continue;

            // ACK immediately — Slack requires ACK within 3 seconds of receipt,
            // before any processing. Same behaviour as @slack/bolt.
            await SendAsync(ws, SlackSocketModeClient.BuildAckPayload(envelope.EnvelopeId), ct);

            // Dispatch to adapter — fire-and-forget for message events so the
            // receive loop is not blocked while the agent streams a response.
            _ = DispatchAsync(envelope, ct);
        }
    }

    private async Task DispatchAsync(SlackSocketEnvelope envelope, CancellationToken ct)
    {
        try
        {
            await adapter.HandleSocketEnvelopeAsync(envelope, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown — not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception dispatching Slack Socket Mode envelope {EnvelopeId} (type={Type}).",
                envelope.EnvelopeId, envelope.Type);
        }
    }
}
