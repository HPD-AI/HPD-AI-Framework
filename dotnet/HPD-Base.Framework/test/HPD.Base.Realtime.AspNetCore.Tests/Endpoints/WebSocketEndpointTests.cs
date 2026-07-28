namespace HPD.Base.Realtime.AspNetCore.Tests.Endpoints;

public sealed class WebSocketEndpointTests
{
    [Fact]
    public async Task PumpFailureIsObservedExactlyOnce()
    {
        var feed = new TrackingRealtimeFeedSource();
        using var logs = new HPD.Base.Tests.Observability.LogCollector();
        await using var app = await TestRealtimeApp.CreateAsync(
            logs: logs,
            configureServices: services => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                services,
                ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);
        await SendJoinAsync(socket, "join", "base:records:items");
        _ = await ReceiveAsync(socket);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        feed.Fail();
        await feed.EnumerationStopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await YieldUntilAsync(() => logs.RecordsFor(5505).Count() == 1);

        logs.RecordsFor(5505).Should().ContainSingle();
    }

    [Fact]
    public async Task DisconnectAwaitsEveryChannelAndReleasesConnection()
    {
        var feed = new TrackingRealtimeFeedSource();
        await using var app = await TestRealtimeApp.CreateAsync(
            configureServices: services => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                services,
                ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);
        await SendJoinAsync(socket, "join", "base:records:items");
        _ = await ReceiveAsync(socket);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
        await feed.EnumerationStopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await YieldUntilAsync(() => app.Services.GetRequiredService<BaseRealtimeStats>().ActiveConnections == 0);

        app.Services.GetRequiredService<BaseRealtimeStats>().ActiveConnections.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateJoinRejectsWithoutReplacingFirstChannel()
    {
        var feed = new TrackingRealtimeFeedSource();
        await using var app = await TestRealtimeApp.CreateAsync(
            configureServices: services => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                services,
                ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendJoinAsync(socket, "first", "base:records:items");
        (await ReceiveAsync(socket)).Type.Should().Be(BaseRealtimeProtocolTypes.Joined);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await SendJoinAsync(socket, "duplicate", "base:records:items");
        var rejected = await ReceiveAsync(socket);

        rejected.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        rejected.Error!.Code.Should().Be(BaseRealtimeErrorCodes.ChannelAlreadyJoined);
        feed.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task JoinRateLimitUsesPerConnectionFixedWindow()
    {
        var time = new ManualTimestampProvider();
        await using var app = await TestRealtimeApp.CreateAsync(
            options => options.Limits = options.Limits with { MaxJoinsPerSecond = 1 },
            configureServices: services =>
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                    services,
                    ServiceDescriptor.Singleton<TimeProvider>(time)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendJoinAsync(socket, "first", "channel-one");
        (await ReceiveAsync(socket)).Type.Should().Be(BaseRealtimeProtocolTypes.Joined);

        await SendJoinAsync(socket, "limited-marker", "channel-two");
        var limited = await ReceiveAsync(socket);
        limited.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        limited.Error!.Code.Should().Be(BaseRealtimeErrorCodes.JoinRateLimited);
        app.Services.GetRequiredService<BaseRealtimeStats>().JoinRateRejections.Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(1));
        await SendJoinAsync(socket, "after-window", "channel-three");
        (await ReceiveAsync(socket)).Type.Should().Be(BaseRealtimeProtocolTypes.Joined);
    }

    [Fact]
    public async Task LeaveAwaitsChannelCompletionBeforeAcknowledgement()
    {
        var feed = new TrackingRealtimeFeedSource();
        await using var app = await TestRealtimeApp.CreateAsync(
            configureServices: services => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                services,
                ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendJoinAsync(socket, "join", "base:records:items");
        _ = await ReceiveAsync(socket);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Leave,
            Ref = "leave",
            Channel = "base:records:items"
        });

        var left = await ReceiveAsync(socket);
        left.Type.Should().Be(BaseRealtimeProtocolTypes.Left);
        feed.EnumerationStopped.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void ConcurrentConnectionReservationsNeverExceedLimit()
    {
        var stats = new BaseRealtimeStats();
        var accepted = 0;

        Parallel.For(0, 64, _ =>
        {
            if (stats.TryRecordConnectionOpened(1))
                Interlocked.Increment(ref accepted);
        });

        accepted.Should().Be(1);
        stats.ActiveConnections.Should().Be(1);
        stats.RecordConnectionClosed();
        stats.ActiveConnections.Should().Be(0);
    }

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
    public async Task DurableDescriptorAndEventCursorReachTheWireContract()
    {
        var feed = new TrackingRealtimeFeedSource
        {
            Replayable = true,
            Resumable = true,
            Cursor = "opaque-join-cursor"
        };
        await using var app = await TestRealtimeApp.CreateAsync(
            configureServices: services =>
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                    services,
                    ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendJoinAsync(socket, "durable", "base:records:items");
        var joined = await ReceiveAsync(socket);

        joined.Join!.Replayable.Should().BeTrue();
        joined.Join.Resumable.Should().BeTrue();
        joined.Join.Cursor.Should().Be("opaque-join-cursor");

        feed.Emit(new BaseRealtimeEvent
        {
            EventId = "event-one",
            Type = BaseEventTypes.RecordCreated,
            SchemaVersion = BaseEventSchemaVersions.V1,
            OccurredAt = DateTimeOffset.UnixEpoch,
            Resource = new BaseRealtimeRecordResource
            {
                CollectionId = "items",
                RecordId = new RecordId("one")
            },
            Operation = BaseOperationKind.Create,
            Cursor = "opaque-event-cursor"
        });
        var evt = await ReceiveAsync(socket);

        evt.Event!.Cursor.Should().Be("opaque-event-cursor");
    }

    [Fact]
    public async Task DurableOversizeEventTerminatesChannelBeforeAdvancingCursor()
    {
        var feed = new TrackingRealtimeFeedSource
        {
            Replayable = true,
            Resumable = true,
            Cursor = "opaque-join-cursor"
        };
        await using var app = await TestRealtimeApp.CreateAsync(
            options => options.Limits = options.Limits with { MaxPayloadBytes = 256 },
            configureServices: services =>
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                    services,
                    ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);
        await SendJoinAsync(socket, "durable", "base:records:items");
        _ = await ReceiveAsync(socket);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        feed.Emit(new BaseRealtimeEvent
        {
            EventId = new string('e', 512),
            Type = BaseEventTypes.RecordCreated,
            SchemaVersion = BaseEventSchemaVersions.V1,
            OccurredAt = DateTimeOffset.UnixEpoch,
            Resource = new BaseRealtimeRecordResource
            {
                CollectionId = "items",
                RecordId = new RecordId("oversize")
            },
            Operation = BaseOperationKind.Create,
            Cursor = "cursor-for-oversize-event"
        });
        feed.Emit(new BaseRealtimeEvent
        {
            EventId = "later-event",
            Type = BaseEventTypes.RecordCreated,
            SchemaVersion = BaseEventSchemaVersions.V1,
            OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            Resource = new BaseRealtimeRecordResource
            {
                CollectionId = "items",
                RecordId = new RecordId("later")
            },
            Operation = BaseOperationKind.Create,
            Cursor = "cursor-that-must-not-advance"
        });
        var terminal = await ReceiveAsync(socket);

        terminal.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        terminal.Error!.Code.Should().Be(BaseRealtimeErrorCodes.PayloadTooLarge);
        terminal.Channel.Should().Be("base:records:items");
        await feed.EnumerationStopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RetentionOvertakeSendsStableCursorExpiredError()
    {
        var feed = new TrackingRealtimeFeedSource
        {
            Replayable = true,
            Resumable = true,
            Cursor = "opaque-join-cursor"
        };
        await using var app = await TestRealtimeApp.CreateAsync(
            configureServices: services =>
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(
                    services,
                    ServiceDescriptor.Singleton<HPD.Base.Realtime.Feeds.IBaseRealtimeFeedSource>(feed)));
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);
        await SendJoinAsync(socket, "durable", "base:records:items");
        _ = await ReceiveAsync(socket);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        feed.Fail(new BaseRealtimeFeedException(
            BaseRealtimeErrorCodes.CursorExpired,
            "The durable realtime cursor is older than the retained mutation journal."));
        var terminal = await ReceiveAsync(socket);

        terminal.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        terminal.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CursorExpired);
        terminal.Channel.Should().Be("base:records:items");
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

    [Theory]
    [InlineData("connect")]
    [InlineData("authenticate")]
    public async Task RemovedClientCommandsAreRejected(string removedType)
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        using var socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = removedType,
            Ref = "1"
        });

        var error = await ReceiveAsync(socket);
        error.Type.Should().Be(BaseRealtimeProtocolTypes.Error);
        error.Error!.Code.Should().Be(BaseRealtimeErrorCodes.ProtocolInvalid);
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
    public async Task ReceiveIdleTimeoutClosesSocket()
    {
        await using var app = await TestRealtimeApp.CreateAsync(options => options.Limits = options.Limits with { ReceiveIdleTimeoutSeconds = 1 });
        using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocket), CancellationToken.None);
        _ = await ReceiveAsync(socket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, cts.Token);

        result.MessageType.Should().Be(WebSocketMessageType.Close);
        app.Services.GetRequiredService<BaseRealtimeStats>().ReceiveIdleTimeouts.Should().Be(1);
    }

    private static async Task SendAsync(WebSocket socket, BaseRealtimeClientMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static Task SendJoinAsync(WebSocket socket, string @ref, string channel) =>
        SendAsync(socket, new BaseRealtimeClientMessage
        {
            Type = BaseRealtimeProtocolTypes.Join,
            Ref = @ref,
            Channel = channel,
            Config = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                Private = false,
                CollectionId = "items"
            }
        });

    private static async Task YieldUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1024 && !condition(); attempt++)
            await Task.Yield();

        condition().Should().BeTrue();
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

    private sealed class ManualTimestampProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.UnixEpoch.AddTicks(Volatile.Read(ref _timestamp));

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan elapsed) =>
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
