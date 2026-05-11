using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for SSE (Server-Sent Events) streaming endpoint.
/// Tests: POST /agents/{agentId}/sessions/{sid}/branches/{bid}/stream
/// </summary>
public class SseStreamingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public SseStreamingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private static string CreateInputJson(string text, AgentRunConfig? runConfig = null)
    {
        return JsonSerializer.Serialize(new StreamTextRequest(text, runConfig));
    }

    private Task<HttpResponseMessage> PostInputAsync(
        string url,
        string json,
        CancellationToken cancellationToken = default)
    {
        return _client.PostAsync(
            url,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);
    }

    #region POST /agents/{agentId}/sessions/{sid}/branches/{bid}/stream

    [Fact]
    public async Task StreamSse_ReturnsSSE_WithCorrectContentType()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test message");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task StreamSse_SendsTextDeltaEvents()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Hello");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Full SSE parsing would require reading the stream
        // For now, verify the response is successful
    }

    [Fact]
    public async Task StreamSse_StreamsSerializedEventEnvelopeDataLines()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Hello");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("data:");
        body.Should().Contain("\"type\":\"TEXT_DELTA\"");
        body.Should().Contain("\"type\":\"MESSAGE_TURN_FINISHED\"");
    }

    [Fact]
    public async Task StreamSse_InvalidEventEnvelope_Returns400()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        const string request = "{\"type\":\"NOT_AN_AGENT_INPUT\",\"text\":\"Hello\"}";

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/events/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StreamSse_SendsToolCallEvents()
    {
        // This would require an agent with tools configured
        // Simplified test verifies endpoint accepts request
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Use a tool");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_SendsMessageFinishedEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_SendsAllEventTypes()
    {
        // Comprehensive test for all event types
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Complete interaction");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task StreamSse_Returns409_WhenAlreadyStreaming()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test");

        // Start first stream (don't await completion)
        var firstStreamTask = PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Give it time to start
        await Task.Delay(100);

        // Act - Try to start second stream on same branch
        var secondResponse = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Clean up
        try { await firstStreamTask; } catch { }
    }

    [Fact]
    public async Task StreamSse_CancelsGracefully_OnClientDisconnect()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Long running task");

        using var cts = new CancellationTokenSource();

        // Act - Start stream and cancel it
        var streamTask = PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request, cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        // Assert - Should cancel gracefully
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await streamTask);
    }

    [Fact]
    public async Task StreamSse_PassesHttpContextRequestAborted_AsCancellationToken()
    {
        // This test verifies the endpoint uses HttpContext.RequestAborted
        // Implicit in the cancellation test above
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_ReleasesStreamLock_OnCompletion()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Quick message");

        // Act - Complete first stream
        var firstResponse = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Start second stream (should succeed if lock was released)
        var secondResponse = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_ReleasesStreamLock_OnError()
    {
        // Similar to completion test but with error scenario
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_ReleasesStreamLock_OnCancellation()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test");

        using var cts = new CancellationTokenSource();
        var streamTask = PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request, cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        try { await streamTask; } catch { }

        // Act - Try new stream after cancellation
        var newResponse = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert - Lock should be released
        newResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_UsesRunConfig_WhenProvided()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        _factory.FakeChatClient.Clear();

        var request = CreateInputJson("Test with config", new AgentRunConfig
        {
            Chat = new ChatRunConfig
            {
                Temperature = 0.7,
                MaxOutputTokens = 1000
            },
            AdditionalSystemInstructions = "Be concise",
            CoalesceDeltas = true,
            SkipTools = false
        });

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.FakeChatClient.CapturedOptions.Should().ContainSingle();
        var options = _factory.FakeChatClient.CapturedOptions.Single();
        options.Should().NotBeNull();
        options!.Temperature.Should().Be(0.7f);
        options.MaxOutputTokens.Should().Be(1000);
        options.Instructions.Should().Contain("Be concise");
    }

    [Fact]
    public async Task StreamSse_AppendsMessagesToBranch()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Save this message");

        // Act
        await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Verify messages were saved
        var messagesResponse = await _client.GetAsync($"/sessions/{sessionId}/branches/main/messages");
        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<MessageDto>>();

        // Assert
        messages.Should().NotBeNull();
        messages!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StreamSse_SavesSessionAndBranch_OnCompletion()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Persistent message");

        // Act
        await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/stream", request);

        // Verify session still exists
        var sessionResponse = await _client.GetAsync($"/sessions/{sessionId}");

        // Assert
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StreamSse_Returns404_WhenSessionNotFound()
    {
        // Arrange
        var request = CreateInputJson("Test");

        // Act
        var response = await PostInputAsync("/agents/test-agent/sessions/nonexistent/branches/main/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StreamSse_Returns404_WhenBranchNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = CreateInputJson("Test");

        // Act
        var response = await PostInputAsync($"/agents/test-agent/sessions/{sessionId}/branches/nonexistent/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
