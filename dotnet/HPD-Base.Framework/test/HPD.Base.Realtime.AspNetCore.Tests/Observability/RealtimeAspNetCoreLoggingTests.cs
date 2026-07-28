using HPD.Base.Realtime.AspNetCore.Observability.Logging;
using HPD.Base.Tests.Observability;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Realtime.AspNetCore.Tests.Observability;

public sealed class RealtimeAspNetCoreLoggingTests
{
    [Fact]
    public void RegistryContainsOnlyActiveRealtimeContracts()
    {
        HPDBaseLogEventRegistry.Active
            .Where(contract => contract.Owner.StartsWith("HPD.Base.Realtime", StringComparison.Ordinal))
            .Select(contract => contract.Id)
            .Should().Equal(5000, 5001, 5500, 5501, 5503, 5504, 5505, 5506, 5508, 5509, 5510, 5511);
    }

    [Fact]
    public async Task PreflightRejectionUsesExactSafeContract()
    {
        using var logs = new LogCollector();
        await using var app = await TestRealtimeApp.CreateAsync(logs: logs);

        _ = await app.GetTestClient().GetAsync(BaseRealtimeRoutes.WebSocket);

        var record = logs.RecordsFor(5509).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.OriginalFormat.Should().Be(
            "A realtime WebSocket connection was rejected ({ErrorCode}).");
        Property(record, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.ProtocolInvalid);
        LogSafetyInspector.AssertSafe([record]);
    }

    [Fact]
    public async Task ProtocolAndPolicyJoinRejectionsHaveDistinctOwners()
    {
        using var logs = new LogCollector();
        await using var app = await TestRealtimeApp.CreateAsync(logs: logs);
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "secret-ref",
            Channel = "secret-channel",
            Config = new BaseRealtimeChannelJoinRequest { Kind = "unsupported", Private = false }
        });
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "secret-ref",
            Channel = "secret-channel",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                TenantId = "secret-tenant"
            }
        });
        _ = await ReceiveAsync(socket);

        var protocol = logs.RecordsFor(5500).Should().ContainSingle().Subject;
        var policy = logs.RecordsFor(5508).Should().ContainSingle().Subject;
        Property(protocol, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.ChannelUnsupported);
        Property(policy, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.AuthRequired);
        foreach (var record in new[] { protocol, policy })
        {
            record.Exception.Should().BeNull();
            record.RenderedMessage.Should().NotContain("secret-");
        }
    }

    [Fact]
    public async Task PayloadDropUsesBoundedBucketAndDoesNotExposePayload()
    {
        using var logs = new LogCollector();
        await using var app = await TestRealtimeApp.CreateAsync(
            options => options.Limits = options.Limits with { MaxMessageBytes = 32 },
            logs);
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        var secret = new string('x', 128);
        var bytes = Encoding.UTF8.GetBytes("{\"type\":\"join\",\"channel\":\"" + secret + "\"}");
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await ReceiveAsync(socket);

        var record = logs.RecordsFor(5503).Should().ContainSingle().Subject;
        Property(record, "PayloadSizeBucket").Should().Be("1-1KiB");
        record.RenderedMessage.Should().NotContain(secret);
        LogSafetyInspector.AssertSafe([record]);
    }

    [Fact]
    public async Task SuccessfulConnectionDoesNotEmitFailureEvents()
    {
        using var logs = new LogCollector();
        await using var app = await TestRealtimeApp.CreateAsync(logs: logs);
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        logs.RecordsFor(5506).Should().ContainSingle();
        logs.Records.Where(record => record.EventId.Id is >= 5500 and <= 5509)
            .Should().OnlyContain(record => record.EventId.Id == 5506);
    }

    [Fact]
    public async Task ReceiveIdleTimeoutUsesExactSafeContract()
    {
        using var logs = new LogCollector();
        await using var app = await TestRealtimeApp.CreateAsync(
            options => options.Limits = options.Limits with { ReceiveIdleTimeoutSeconds = 1 },
            logs);
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(new byte[1024], cts.Token);

        result.MessageType.Should().Be(WebSocketMessageType.Close);
        var record = logs.RecordsFor(5510).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.OriginalFormat.Should().Be(
            "A realtime WebSocket connection exceeded its receive-idle limit ({ErrorCode}).");
        Property(record, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.ConnectionIdleTimeout);
        LogSafetyInspector.AssertSafe([record]);
    }

    [Fact]
    public void SlowConsumerTerminationUsesExactSafeContract()
    {
        using var logs = new LogCollector();

        HPDBaseRealtimeAspNetCoreLog.SlowConsumerTerminated(
            logs.CreateLogger<BaseRealtimeWebSocketSession>(),
            BaseRealtimeErrorCodes.ConsumerSlow);

        var record = logs.RecordsFor(5511).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.OriginalFormat.Should().Be(
            "A realtime channel was terminated because its consumer was too slow ({ErrorCode}).");
        Property(record, "ErrorCode").Should().Be(BaseRealtimeErrorCodes.ConsumerSlow);
        record.Exception.Should().BeNull();
        LogSafetyInspector.AssertSafe([record]);
    }

    private static async Task SendAsync(WebSocket socket, BaseRealtimeClientMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            message,
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<BaseRealtimeServerMessage> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        result.MessageType.Should().Be(WebSocketMessageType.Text);
        return JsonSerializer.Deserialize(
            Encoding.UTF8.GetString(buffer, 0, result.Count),
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage)!;
    }

    private static object? Property(CapturedLogRecord record, string name) =>
        record.State.Single(property => property.Key == name).Value;
}
