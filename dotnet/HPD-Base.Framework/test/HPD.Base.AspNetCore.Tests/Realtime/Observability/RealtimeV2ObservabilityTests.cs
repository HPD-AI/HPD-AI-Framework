using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using HPD.Base.Tests.Observability;
using Microsoft.Extensions.Logging;

namespace HPD.Base.AspNetCore.Tests.Realtime.Observability;

public sealed class RealtimeV2ObservabilityTests
{
    [Fact]
    public async Task V2PreflightRejectionUsesTheSafeLoggingContract()
    {
        using var logs = new LogCollector();
        await using WebApplication app = await TestRealtimeApp.CreateAsync(logs: logs);
        _ = await app.GetTestClient().GetAsync(BaseRealtimeRoutes.WebSocketV2);

        CapturedLogRecord record = logs.RecordsFor(5509).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.Exception.Should().BeNull();
        Property(record, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.ProtocolInvalid);
        LogSafetyInspector.AssertSafe([record]);
    }

    [Fact]
    public async Task V2ReceiveIdleTimeoutIsBoundedAndSafelyLogged()
    {
        using var logs = new LogCollector();
        await using WebApplication app = await TestRealtimeApp.CreateAsync(options => options.Limits = options.Limits with { ReceiveIdleTimeoutSeconds = 1 }, logs);
        using WebSocket socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocketV2), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        BaseRealtimeErrorMessage terminal = (BaseRealtimeErrorMessage)await ReceiveAsync(socket);
        terminal.Terminal.Should().BeTrue();
        terminal.Error.Code.Should().Be(BaseRealtimeErrorCodes.ConnectionIdleTimeout);
        WebSocketReceiveResult result = await socket.ReceiveAsync(new byte[1024], timeout.Token);
        result.MessageType.Should().Be(WebSocketMessageType.Close);
        CapturedLogRecord record = logs.RecordsFor(5510).Should().ContainSingle().Subject;
        Property(record, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.ConnectionIdleTimeout);
        LogSafetyInspector.AssertSafe([record]);
    }

    [Fact]
    public async Task V2UsesShortSafeConnectionAndJoinTelemetry()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.RealtimeAspNetCore);
        using var metrics = new MeterCollector(HPDBaseMeterNames.RealtimeAspNetCore);
        await using WebApplication app = await TestRealtimeApp.CreateAsync();
        using WebSocket socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocketV2), CancellationToken.None);
        var welcome = (BaseRealtimeWelcomeMessage)await ReceiveAsync(socket);
        await SendAsync(socket, new BaseRealtimeJoinMessage
        {
            ConnectionId = welcome.ConnectionId,
            ConnectionEpoch = welcome.ConnectionEpoch,
            Ref = "secret-ref",
            Channel = new BaseRealtimeLiveFeedRequest { Collection = "items", Filter = new BaseRealtimeRecordFeedFilter() }
        });
        _ = await ReceiveAsync(socket);

        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeWebSocketAccept);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeChannelJoin);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeMessagesReceived);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeMessagesSent);
        activities.Stopped.SelectMany(activity => activity.TagObjects).Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture)).Should().NotContain("secret-ref");
    }

    private static async Task SendAsync(WebSocket socket, BaseRealtimeClientMessage message) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<BaseRealtimeServerMessage> ReceiveAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        byte[] buffer = new byte[64 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, timeout.Token);
        result.MessageType.Should().Be(WebSocketMessageType.Text);
        return JsonSerializer.Deserialize(buffer.AsSpan(0, result.Count), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage)!;
    }

    private static object? Property(CapturedLogRecord record, string name) => record.State.Single(property => property.Key == name).Value;
}
