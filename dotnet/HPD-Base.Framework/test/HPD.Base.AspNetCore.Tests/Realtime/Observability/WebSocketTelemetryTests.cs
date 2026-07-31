using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base;
using HPD.Base.Tests.Observability;

namespace HPD.Base.AspNetCore.Tests.Realtime.Observability;

public sealed class WebSocketTelemetryTests
{
    [Fact]
    public async Task WebSocketTelemetryUsesShortSpansAndDoesNotLeakRealtimeMarkers()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.RealtimeAspNetCore);
        using var metrics = new MeterCollector(HPDBaseMeterNames.RealtimeAspNetCore);
        await using var app = await TestRealtimeApp.CreateAsync();
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);

        _ = await ReceiveAsync(socket);
        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "ref-secret",
            Channel = "channel-secret",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                Private = false,
                CollectionId = "items",
                RecordId = "record-secret",
                IncludeSnapshots = true
            }
        });
        _ = await ReceiveAsync(socket);

        await app.Services.GetRequiredService<IEventPublisher>().EmitAsync(TestRealtimeApp.Event() with
        {
            EventId = "event-secret",
            Resource = new EventResource
            {
                Kind = EventResourceKind.Record,
                CollectionId = "items",
                RecordId = new RecordId("record-secret")
            },
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("record-secret"),
                Payload = Payload("payload-secret"),
                Metadata = new RecordMetadata()
            }
        });
        _ = await ReceiveAsync(socket);

        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeWebSocketAccept);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeConnection);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeChannelJoin);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeEventSend);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeMessagesReceived);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeMessagesSent);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeMessageBytes);

        var forbidden = new[] { "channel-secret", "ref-secret", "record-secret", "event-secret", "payload-secret" };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
    }

    private static async Task SendAsync(WebSocket socket, BaseRealtimeClientMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<BaseRealtimeServerMessage> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            result.MessageType.Should().Be(WebSocketMessageType.Text);
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        return JsonSerializer.Deserialize(json, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage)!;
    }

    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

}
