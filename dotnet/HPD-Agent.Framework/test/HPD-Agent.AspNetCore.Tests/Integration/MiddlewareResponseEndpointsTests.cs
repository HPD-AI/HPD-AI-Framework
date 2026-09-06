using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Serialization;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for middleware response endpoints using event-based responses.
/// Tests: POST /agents/{agentId}/sessions/{sid}/threads/{bid}/responses
/// </summary>
public class MiddlewareResponseEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MiddlewareResponseEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    #region POST /agents/{agentId}/sessions/{sid}/threads/{bid}/responses

    [Fact]
    public async Task Respond_AcceptsPermissionResponseEvent_ReturnsTypedNotFoundWhenRequestIsAbsent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-123",
            SourceName: "TestSource",
            Approved: true,
            Reason: "Approved for testing",
            Choice: PermissionChoice.Ask);

        // Act
        var response = await PostEventAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/responses",
            evt);

        // Assert - the thread exists, but no live thread runtime is waiting for this response.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AgentRespondResult>();
        body!.Status.Should().Be(AgentRespondStatus.NotFound);
    }

    [Fact]
    public async Task Respond_AcceptsContinuationResponseEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ContinuationResponseEvent(
            ContinuationId: "cont-123",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await PostEventAsync(
             $"/agents/test-agent/sessions/{sessionId}/threads/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Respond_AcceptsQuestionResponseEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new QuestionResponseEvent(
            RequestId: "clar-456",
            SourceName: "TestSource",
            Outcome: QuestionOutcome.Answered,
            Answers: [new("environment", [], "staging")]);

        // Act
        var response = await PostEventAsync(
             $"/agents/test-agent/sessions/{sessionId}/threads/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Respond_AcceptsClientToolOutcomeEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ClientToolInvokeOutcomeEvent
        {
            RequestId = "tool-req-123",
            Outcome = ClientToolInvokeOutcomeKind.Completed,
            Content = new[] { new TextContent("Tool execution succeeded") }
        };
        var json = HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec.Serialize(evt);
        HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec.DeserializeEvent(json).Should().BeOfType<ClientToolInvokeOutcomeEvent>(json);

        // Act
        var response = await _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/responses",
            new StringContent(json, Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Respond_Returns404_WhenSessionMissing()
    {
        // Arrange
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-missing-session",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await PostEventAsync(
            "/agents/test-agent/sessions/missing-session/threads/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Respond_Returns404_WhenThreadMissing()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-missing-thread",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await PostEventAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/missing-thread/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Respond_Returns400_ForNonResponseEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new TextDeltaEvent("not a response", "message-1");

        // Act
        var response = await PostEventAsync(
             $"/agents/test-agent/sessions/{sessionId}/threads/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Respond_Returns400_ForInvalidEnvelope()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/responses",
            new StringContent("{\"hello\":\"world\"}", Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    private Task<HttpResponseMessage> PostEventAsync(string requestUri, AgentEvent evt) =>
        _client.PostAsync(
            requestUri,
            new StringContent(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec.Serialize(evt), Encoding.UTF8, "application/json"));
}
