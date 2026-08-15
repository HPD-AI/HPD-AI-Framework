using System.Net.WebSockets;
using System.Text.Json;

namespace HPD.Base.AspNetCore.Tests.Realtime.Endpoints;

public sealed class WebSocketV2EndpointTests
{
    [Fact]
    public async Task V2WelcomesBeforeAcceptingEpochBoundMessages()
    {
        await using WebApplication app = await TestRealtimeApp.CreateAsync();
        using WebSocket socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocketV2), CancellationToken.None);

        BaseRealtimeWelcomeMessage welcome = (BaseRealtimeWelcomeMessage)await ReceiveAsync(socket);
        welcome.Protocol.Should().Be(2);
        welcome.ConnectionId.Should().NotBeNullOrWhiteSpace();
        welcome.ConnectionEpoch.Should().NotBeNullOrWhiteSpace();

        await SendAsync(socket, new BaseRealtimeHeartbeatMessage
        {
            ConnectionId = welcome.ConnectionId,
            ConnectionEpoch = welcome.ConnectionEpoch,
            HeartbeatId = "heartbeat-1"
        });
        BaseRealtimeHeartbeatAckMessage ack = (BaseRealtimeHeartbeatAckMessage)await ReceiveAsync(socket);
        ack.HeartbeatId.Should().Be("heartbeat-1");
    }

    [Fact]
    public async Task V2JoinsClosedLiveRecordVariant()
    {
        await using WebApplication app = await TestRealtimeApp.CreateAsync();
        using WebSocket socket = await app.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocketV2), CancellationToken.None);
        BaseRealtimeWelcomeMessage welcome = (BaseRealtimeWelcomeMessage)await ReceiveAsync(socket);

        await SendAsync(socket, new BaseRealtimeJoinMessage
        {
            ConnectionId = welcome.ConnectionId,
            ConnectionEpoch = welcome.ConnectionEpoch,
            Ref = "items-live",
            Channel = new BaseRealtimeLiveFeedRequest
            {
                Collection = "items",
                Filter = new BaseRealtimeRecordFeedFilter()
            }
        });

        BaseRealtimeJoinedMessage joined = (BaseRealtimeJoinedMessage)await ReceiveAsync(socket);
        joined.Ref.Should().Be("items-live");
        joined.Delivery.Should().Be("live-at-most-once");
        joined.ChannelEpoch.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task V1RouteDoesNotExist()
    {
        await using WebApplication app = await TestRealtimeApp.CreateAsync();
        using HttpResponseMessage response = await app.GetTestClient().GetAsync("/base/realtime/v1/socket");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicatePropertiesTerminateTheV2Connection()
    {
        await using WebApplication app = await TestRealtimeApp.CreateAsync();
        using WebSocket socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocketV2), CancellationToken.None);
        _ = await ReceiveAsync(socket);
        byte[] invalid = "{\"kind\":\"heartbeat\",\"protocol\":2,\"protocol\":2,\"connectionId\":\"x\",\"connectionEpoch\":\"y\",\"heartbeatId\":\"z\"}"u8.ToArray();
        await socket.SendAsync(invalid, WebSocketMessageType.Text, true, CancellationToken.None);
        BaseRealtimeErrorMessage error = (BaseRealtimeErrorMessage)await ReceiveAsync(socket);
        error.Terminal.Should().BeTrue();
        error.Error.Code.Should().Be(BaseRealtimeErrorCodes.ProtocolInvalid);
        byte[] buffer = new byte[128];
        WebSocketReceiveResult closed = await socket.ReceiveAsync(buffer, CancellationToken.None);
        closed.MessageType.Should().Be(WebSocketMessageType.Close);
    }

    private static async Task SendAsync(WebSocket socket, BaseRealtimeClientMessage message)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<BaseRealtimeServerMessage> ReceiveAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        byte[] buffer = new byte[64 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, timeout.Token);
        result.MessageType.Should().Be(WebSocketMessageType.Text);
        return JsonSerializer.Deserialize(buffer.AsSpan(0, result.Count), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage)!;
    }
}
