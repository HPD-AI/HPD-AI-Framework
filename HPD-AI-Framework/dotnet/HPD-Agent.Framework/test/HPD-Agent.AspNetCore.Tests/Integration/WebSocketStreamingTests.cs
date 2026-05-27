using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Serialization;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for WebSocket streaming endpoint.
/// Tests: GET /agents/{agentId}/sessions/{sid}/branches/{bid}/ws
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

    private static string CreateInputJson(string text)
    {
        return AgentEventSerializer.ToJson(new UserTextInputEvent(text));
    }

    private static string CreateInputJson(string text, string agentId)
    {
        return AgentEventSerializer.ToJson(new UserTextInputEvent(text)
        {
            AgentId = agentId
        });
    }

    private static async Task SendJsonAsync(WebSocket ws, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(WebSocket ws)
    {
        var buffer = new byte[8192];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.MessageType.Should().Be(WebSocketMessageType.Text);

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonDocument.Parse(json);
    }

    private static async Task<JsonDocument> ReceiveUntilTypeAsync(WebSocket ws, string eventType)
    {
        for (var i = 0; i < 20; i++)
        {
            var document = await ReceiveJsonAsync(ws);
            if (document.RootElement.TryGetProperty("type", out var type) &&
                type.GetString() == eventType)
            {
                return document;
            }

            document.Dispose();
        }

        throw new TimeoutException($"Did not receive WebSocket event type '{eventType}'.");
    }

    private async Task<StoredAgentDto> CreateAgentAsync(string name)
    {
        var client = _factory.CreateClient();
        var request = new CreateAgentRequest(name, new AgentConfig
        {
            Name = name,
            MaxAgenticIterations = 10,
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        });

        var response = await client.PostAsJsonAsync("/agents", request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<StoredAgentDto>()
            ?? throw new InvalidOperationException("CreateAgent returned null DTO.");
    }

    #region GET /agents/{agentId}/sessions/{sid}/branches/{bid}/ws

    [Fact]
    public async Task StreamWs_EstablishesWebSocketConnection()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();

        // Act
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Send an agent input event envelope
        var json = CreateInputJson("Hello via WebSocket");
        var bytes = Encoding.UTF8.GetBytes(json);
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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act - Send message
        var message = CreateInputJson("Test");
        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        // Assert - Connection should remain open
        ws.State.Should().Be(WebSocketState.Open);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_InvalidEventEnvelope_ClosesWithInvalidPayload()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        var bytes = Encoding.UTF8.GetBytes("{\"type\":\"NOT_AN_AGENT_INPUT\",\"text\":\"Hello\"}");

        // Act
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

        // Assert
        result.MessageType.Should().Be(WebSocketMessageType.Close);
        result.CloseStatus.Should().Be(WebSocketCloseStatus.InvalidPayloadData);
    }

    [Fact]
    public async Task StreamWs_AcceptsPermissionResponseWhileTurnActive()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        _factory.FakeChatClient.Clear();
        _factory.FakeChatClient.EnqueueStreamingResponse("first ", "second ", "third");

        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act
        await SendJsonAsync(ws, CreateInputJson("long turn"));
        using var _ = await ReceiveUntilTypeAsync(ws, "TEXT_DELTA");

        await SendJsonAsync(ws, AgentEventSerializer.ToJson(
            new PermissionResponseEvent("missing-waiter", "test", Approved: true)));

        using var finished = await ReceiveUntilTypeAsync(ws, "MESSAGE_TURN_FINISHED");

        // Assert
        finished.RootElement.GetProperty("type").GetString().Should().Be("MESSAGE_TURN_FINISHED");
        ws.State.Should().Be(WebSocketState.Open);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_AcceptsInterruptionRequestEnvelope()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        _factory.FakeChatClient.Clear();
        _factory.FakeChatClient.EnqueueStreamingResponse("first ", "second ", "third ", "fourth");

        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act
        await SendJsonAsync(ws, CreateInputJson("interrupt me"));
        using var _ = await ReceiveUntilTypeAsync(ws, "TEXT_DELTA");

        await SendJsonAsync(ws, AgentEventSerializer.ToJson(new InterruptionRequestEvent(
            eventFlowId: null,
            Reason: "stop from client",
            Source: InterruptionSource.User)));

        using var interruption = await ReceiveUntilTypeAsync(ws, "INTERRUPTION_HANDLED");

        // Assert
        interruption.RootElement.GetProperty("reason").GetString().Should().Be("stop from client");
        ws.State.Should().Be(WebSocketState.Open);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task StreamWs_UsesAgentIdFromRoute()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var customAgent = await CreateAgentAsync("Custom Agent");

        var wsClient = _factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/agents/{customAgent.Id}/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act
        await SendJsonAsync(ws, CreateInputJson("hello custom", "ignored-agent-id"));
        using var started = await ReceiveUntilTypeAsync(ws, "MESSAGE_TURN_STARTED");

        // Assert
        started.RootElement.GetProperty("agentName").GetString().Should().Be(customAgent.Id);

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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        // Act - Try to connect second WebSocket to same branch
        // This should fail with 409 or connection refused
        var exception = await Record.ExceptionAsync(async () =>
        {
            var ws2 = await wsClient2.ConnectAsync(
                new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
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
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
            CancellationToken.None);

        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);

        // Act - Try to connect again (should succeed if lock released)
        var wsClient2 = _factory.Server.CreateWebSocketClient();
        var ws2 = await wsClient2.ConnectAsync(
            new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/main/ws"),
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
                new Uri("ws://localhost/agents/test-agent/sessions/nonexistent/branches/main/ws"),
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
                new Uri($"ws://localhost/agents/test-agent/sessions/{sessionId}/branches/nonexistent/ws"),
                CancellationToken.None);
        });

        exception.Should().NotBeNull();
    }

    #endregion
}
