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
/// Tests: POST /agents/{agentId}/sessions/{sid}/branches/{bid}/responses
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

    #region POST /agents/{agentId}/sessions/{sid}/branches/{bid}/responses

    [Fact]
    public async Task Respond_AcceptsPermissionResponseEvent_ReturnsConflictWhenRuntimeInactive()
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
            $"/agents/test-agent/sessions/{sessionId}/branches/main/responses",
            evt);

        // Assert - the branch exists, but no live branch runtime is waiting for this response.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").TryGetProperty("BranchRuntimeNotActive", out _).Should().BeTrue();
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
             $"/agents/test-agent/sessions/{sessionId}/branches/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Respond_AcceptsClarificationResponseEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ClarificationResponseEvent(
            RequestId: "clar-456",
            SourceName: "TestSource",
            Question: "Which environment?",
            Answer: "staging");

        // Act
        var response = await PostEventAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Respond_AcceptsClientToolResponseEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ClientToolInvokeResponseEvent(
            RequestId: "tool-req-123",
            Content: new[] { new TextContent("Tool execution succeeded") },
            Success: true);

        // Act
        var response = await PostEventAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
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
            "/agents/test-agent/sessions/missing-session/branches/main/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Respond_Returns404_WhenBranchMissing()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-missing-branch",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await PostEventAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/missing-branch/responses",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Respond_Returns400_ForNonBidirectionalEvent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new TextDeltaEvent("not a response", "message-1");

        // Act
        var response = await PostEventAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/responses",
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
            $"/agents/test-agent/sessions/{sessionId}/branches/main/responses",
            new StringContent("{\"hello\":\"world\"}", Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    private Task<HttpResponseMessage> PostEventAsync(string requestUri, AgentEvent evt) =>
        _client.PostAsync(
            requestUri,
            new StringContent(AgentEventSerializer.ToJson(evt), Encoding.UTF8, "application/json"));
}
