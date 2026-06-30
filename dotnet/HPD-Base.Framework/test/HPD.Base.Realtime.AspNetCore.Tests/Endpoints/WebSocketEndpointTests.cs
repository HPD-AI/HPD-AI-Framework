namespace HPD.Base.Realtime.AspNetCore.Tests.Endpoints;

public sealed class WebSocketEndpointTests
{
    [Fact]
    public async Task NonWebSocketRequestIsRejectedBeforeUpgrade()
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync(BaseRealtimeRoutes.WebSocket);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(BaseRealtimeErrorCodes.ProtocolInvalid);
    }

    [Fact]
    public async Task WebSocketConnectJoinAndReceiveProjectedEvent()
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);

        var connected = await ReceiveAsync(socket);
        connected.Type.Should().Be(BaseRealtimeProtocolTypes.Connected);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "1",
            Channel = "base:records:items",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                Private = false,
                CollectionId = "items",
                IncludeSnapshots = true
            }
        });

        var joined = await ReceiveAsync(socket);
        joined.Type.Should().Be(BaseRealtimeProtocolTypes.Joined);
        joined.Join!.Replayable.Should().BeFalse();
        joined.Join.Resumable.Should().BeFalse();

        await app.Services.GetRequiredService<IEventPublisher>().EmitAsync(TestRealtimeApp.Event());

        var evt = await ReceiveAsync(socket);
        evt.Type.Should().Be(BaseRealtimeProtocolTypes.Event);
        evt.Event!.Resource.CollectionId.Should().Be("items");
        evt.Event.After.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsupportedChannelKindReturnsProtocolError()
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "1",
            Channel = "bad",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = "base.live_query",
                Private = false
            }
        });

        var error = await ReceiveAsync(socket);
        error.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        error.Error!.Code.Should().Be(BaseRealtimeErrorCodes.ChannelUnsupported);
    }

    [Fact]
    public async Task JoinWithUnauthorizedTenantReturnsChannelUnauthorized()
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "1",
            Channel = "base:records:items",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                Private = false,
                CollectionId = "items",
                TenantId = "tenant-a"
            }
        });

        var error = await ReceiveAsync(socket);
        error.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        error.Error!.Code.Should().Be(BaseRealtimeErrorCodes.ChannelUnauthorized);
    }

    [Fact]
    public async Task PayloadTooLargeReturnsProtocolError()
    {
        await using var app = await TestRealtimeApp.CreateAsync(options => options.Limits = options.Limits with { MaxMessageBytes = 32 });
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        var bytes = Encoding.UTF8.GetBytes("{\"type\":\"join\",\"ref\":\"1\",\"channel\":\"" + new string('x', 128) + "\"}");
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var error = await ReceiveAsync(socket);
        error.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        error.Error!.Code.Should().Be(BaseRealtimeErrorCodes.PayloadTooLarge);
    }

    [Fact]
    public async Task OutgoingPayloadLimitDropsSnapshotsBeforeEvent()
    {
        await using var app = await TestRealtimeApp.CreateAsync(options => options.Limits = options.Limits with { MaxPayloadBytes = 500 });
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = "1",
            Channel = "base:records:items",
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                Private = false,
                CollectionId = "items",
                IncludeSnapshots = true
            }
        });
        _ = await ReceiveAsync(socket);

        await app.Services.GetRequiredService<IEventPublisher>().EmitAsync(TestRealtimeApp.Event() with
        {
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("one"),
                Payload = LargePayload(),
                Metadata = new RecordMetadata()
            }
        });

        var evt = await ReceiveAsync(socket);
        evt.Type.Should().Be(BaseRealtimeProtocolTypes.Event);
        evt.Event!.After.Should().BeNull();
        app.Services.GetRequiredService<BaseRealtimeStats>().PayloadLimitDrops.Should().Be(1);
    }

    [Fact]
    public async Task HeartbeatTimeoutClosesSocket()
    {
        await using var app = await TestRealtimeApp.CreateAsync(options => options.Limits = options.Limits with { HeartbeatTimeoutSeconds = 1 });
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, cts.Token);

        result.MessageType.Should().Be(WebSocketMessageType.Close);
        app.Services.GetRequiredService<BaseRealtimeStats>().HeartbeatTimeouts.Should().Be(1);
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

    private static RecordPayload LargePayload()
    {
        using var document = JsonDocument.Parse("{\"title\":\"" + new string('x', 2048) + "\"}");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }
}
