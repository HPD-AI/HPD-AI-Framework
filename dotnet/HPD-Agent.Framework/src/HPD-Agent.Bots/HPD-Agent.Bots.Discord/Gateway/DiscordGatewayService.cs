using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Bots.Discord.Payloads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Discord.Gateway;

public sealed class DiscordGatewayService(
    IOptions<DiscordBotConfig> options,
    DiscordGatewayClient client,
    DiscordApiClient api,
    ILogger<DiscordGatewayService> logger)
    : BotWebSocketService(logger)
{
    private readonly DiscordBotConfig _config = options.Value;
    private readonly DiscordGatewayClient _client = client;
    private readonly DiscordApiClient _api = api;
    private readonly ILogger<DiscordGatewayService> _logger = logger;
    private string? _sessionId;
    private int? _lastSequence;

    protected override Task<Uri> GetConnectionUriAsync(CancellationToken ct)
        => _client.GetGatewayUriAsync(ct);

    protected override async Task RunSessionAsync(WebSocket ws, CancellationToken ct)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sessionCts.CancelAfter(_config.GatewaySessionDuration);

        while (!sessionCts.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var frame = await ReceiveFrameAsync(ws, sessionCts.Token);
            if (frame is null) return;
            if (frame.Sequence is not null)
                _lastSequence = frame.Sequence;

            switch (frame.Op)
            {
                case 10:
                    await StartGatewaySessionAsync(ws, frame.Data, sessionCts.Token);
                    break;

                case 0:
                    await HandleDispatchAsync(frame, sessionCts.Token);
                    break;

                case 7:
                    return;

                case 9:
                    _sessionId = null;
                    _lastSequence = null;
                    return;
            }
        }
    }

    private async Task StartGatewaySessionAsync(WebSocket ws, JsonElement data, CancellationToken ct)
    {
        var heartbeatInterval = data.TryGetProperty("heartbeat_interval", out var interval)
            ? interval.GetInt32()
            : 45_000;

        _ = RunHeartbeatAsync(ws, heartbeatInterval, ct);

        if (_sessionId is not null && _lastSequence is not null)
        {
            await SendJsonAsync(ws, new DiscordGatewayOutgoingFrame(
                Op: 6,
                Data: ToJsonElement(new DiscordGatewayResumePayload(
                    Token: _config.GatewayToken ?? _config.BotToken,
                    SessionId: _sessionId,
                    Sequence: _lastSequence.Value),
                    DiscordBotJsonContext.Default.DiscordGatewayResumePayload)), ct);
            return;
        }

        await SendJsonAsync(ws, new DiscordGatewayOutgoingFrame(
            Op: 2,
            Data: ToJsonElement(new DiscordGatewayIdentifyPayload(
                Token: _config.GatewayToken ?? _config.BotToken,
                Intents: DiscordGatewayIntents.Required,
                Properties: new DiscordGatewayIdentifyProperties(
                    Os: global::System.Environment.OSVersion.Platform.ToString(),
                    Browser: "hpd-agent",
                    Device: "hpd-agent")),
                    DiscordBotJsonContext.Default.DiscordGatewayIdentifyPayload)), ct);
    }

    private async Task RunHeartbeatAsync(WebSocket ws, int intervalMs, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await SendJsonAsync(ws, new DiscordGatewayOutgoingFrame(
                    Op: 1,
                    Data: ToJsonElement(_lastSequence)), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task HandleDispatchAsync(DiscordGatewayFrame frame, CancellationToken ct)
    {
        if (frame.Type == "READY" &&
            frame.Data.TryGetProperty("session_id", out var sessionId))
        {
            _sessionId = sessionId.GetString();
            return;
        }

        var gatewayEvent = frame.Type switch
        {
            "MESSAGE_CREATE" => "GATEWAY_MESSAGE_CREATE",
            "MESSAGE_REACTION_ADD" => "GATEWAY_MESSAGE_REACTION_ADD",
            "MESSAGE_REACTION_REMOVE" => "GATEWAY_MESSAGE_REACTION_REMOVE",
            _ => null,
        };

        if (gatewayEvent is null)
            return;

        if (string.IsNullOrWhiteSpace(_config.GatewayForwardUrl))
        {
            _logger.LogWarning(
                "Discord Gateway received {EventType}, but GatewayForwardUrl is not configured.",
                frame.Type);
            return;
        }

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(
            frame.Data,
            DiscordBotJsonContext.Default.JsonElement);
        await _api.SendGatewayEventAsync(_config.GatewayForwardUrl, gatewayEvent, bodyBytes, ct);
    }

    private static async Task<DiscordGatewayFrame?> ReceiveFrameAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return JsonSerializer.Deserialize(
            Encoding.UTF8.GetString(stream.ToArray()),
            DiscordBotJsonContext.Default.DiscordGatewayFrame);
    }

    private async Task SendJsonAsync(WebSocket ws, DiscordGatewayOutgoingFrame value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            value,
            DiscordBotJsonContext.Default.DiscordGatewayOutgoingFrame);
        await SendAsync(ws, bytes, ct);
    }

    private static JsonElement ToJsonElement<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => JsonSerializer.SerializeToElement(value, jsonTypeInfo);

    private static JsonElement ToJsonElement(int? value)
    {
        using var doc = JsonDocument.Parse(value is null
            ? "null"
            : value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return doc.RootElement.Clone();
    }
}

internal static class DiscordGatewayIntents
{
    public const int Guilds = 1;
    public const int GuildMessages = 512;
    public const int GuildMessageReactions = 1024;
    public const int DirectMessages = 4096;
    public const int DirectMessageReactions = 8192;
    public const int MessageContent = 32768;

    public const int Required =
        Guilds |
        GuildMessages |
        GuildMessageReactions |
        DirectMessages |
        DirectMessageReactions |
        MessageContent;
}

internal record DiscordGatewayFrame(
    [property: JsonPropertyName("op")] int Op,
    [property: JsonPropertyName("d")] JsonElement Data,
    [property: JsonPropertyName("s")] int? Sequence,
    [property: JsonPropertyName("t")] string? Type);

internal record DiscordGatewayOutgoingFrame(
    [property: JsonPropertyName("op")] int Op,
    [property: JsonPropertyName("d")] JsonElement Data);

internal record DiscordGatewayIdentifyPayload(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("intents")] int Intents,
    [property: JsonPropertyName("properties")] DiscordGatewayIdentifyProperties Properties);

internal record DiscordGatewayIdentifyProperties(
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("browser")] string Browser,
    [property: JsonPropertyName("device")] string Device);

internal record DiscordGatewayResumePayload(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("seq")] int Sequence);
