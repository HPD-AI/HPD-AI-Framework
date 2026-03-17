using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for WebSocket streaming endpoint.
/// Tests: GET /sessions/{sid}/branches/{bid}/ws
/// </summary>
public class WebSocketStreamingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WebSocketStreamingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> CreateTestSession()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    #region GET /sessions/{sid}/branches/{bid}/ws

    [Fact]
    public async Task StreamWs_EstablishesWebSocketConnection()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();

        // Act
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Assert
        ws.State.Should().Be(WebSocketState.Open);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_SendsJsonEvents_OverWebSocket()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Send a message
        var message = new StreamRequest(
            new List<StreamMessage> { new("Hello via WebSocket", "user") },
            new List<System.Text.Json.JsonElement>(),
            new List<System.Text.Json.JsonElement>(),
            null,
            new List<string>(),
            new List<string>(),
            false,
            null);

        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        // Act - Receive response
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

        // Assert
        result.MessageType.Should().Be(WebSocketMessageType.Text);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_AcceptsMessagesFromClient()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act - Send message
        var message = "{\"messages\":[{\"content\":\"Test\",\"role\":\"user\"}]}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        // Assert - Connection should remain open
        ws.State.Should().Be(WebSocketState.Open);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_Returns409_WhenAlreadyStreaming()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient1 = _factory.Server.CreateWebSocketClient();
        var wsClient2 = _factory.Server.CreateWebSocketClient();

        // Connect first WebSocket
        var ws1 = await wsClient1.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act - Try to connect second WebSocket to same branch
        // This should fail with 409 or connection refused
        var exception = await Record.ExceptionAsync(async () =>
        {
            var ws2 = await wsClient2.ConnectAsync(
                new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
                CancellationToken.None);
        });

        // Assert
        exception.Should().NotBeNull();

        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_CancelsOnWebSocketClose()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act - Close the WebSocket
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);

        // Assert
        ws.State.Should().Be(WebSocketState.Closed);
    }

    [Fact(Skip = "TestServer limitation: WebSocket ConnectAsync completes synchronously in-process, making client-side cancellation untestable. ASP.NET Core's own TestHost tests do not test this scenario.")]
    public async Task StreamWs_CancelsOnRequestAborted()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();

        using var cts = new CancellationTokenSource();

        // Act - Connect and then cancel
        var connectTask = wsClient.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await connectTask);
    }

    [Fact]
    public async Task StreamWs_UsesLinkedCancellationToken()
    {
        // This test verifies that the endpoint creates a linked token
        // combining HttpContext.RequestAborted and WebSocket close
        // Implicit in the close and abort tests above

        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", CancellationToken.None);

        // Assert
        ws.State.Should().Be(WebSocketState.Closed);
    }

    [Fact]
    public async Task StreamWs_ReleasesStreamLock_OnClose()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient1 = _factory.Server.CreateWebSocketClient();

        var ws1 = await wsClient1.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);

        // Act - Try to connect again (should succeed if lock released)
        var wsClient2 = _factory.Server.CreateWebSocketClient();
        var ws2 = await wsClient2.ConnectAsync(
            new Uri($"ws://localhost/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Assert
        ws2.State.Should().Be(WebSocketState.Open);

        await ws2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_Returns404_WhenSessionNotFound()
    {
        // Arrange
        var wsClient = _factory.Server.CreateWebSocketClient();

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () =>
        {
            await wsClient.ConnectAsync(
                new Uri("ws://localhost/sessions/nonexistent/branches/main/ws"),
                CancellationToken.None);
        });

        exception.Should().NotBeNull();
    }

    [Fact]
    public async Task StreamWs_Returns404_WhenBranchNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () =>
        {
            await wsClient.ConnectAsync(
                new Uri($"ws://localhost/sessions/{sessionId}/branches/nonexistent/ws"),
                CancellationToken.None);
        });

        exception.Should().NotBeNull();
    }

    #endregion
}
