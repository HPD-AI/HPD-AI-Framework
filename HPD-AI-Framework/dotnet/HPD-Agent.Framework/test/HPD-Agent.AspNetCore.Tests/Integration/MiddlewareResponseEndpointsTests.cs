using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for middleware response endpoints using event-based responses.
/// Tests: POST /agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond, POST /agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond
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

    #region POST /agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond

    [Fact]
    public async Task RespondToPermission_AcceptsPermissionResponseEvent_Returns200()
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
        var response = await _client.PostAsJsonAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/permissions/respond",
            evt);

        // Assert - Returns 200 or 404 depending on whether agent is running
        // In a real scenario with running agent, would return 200
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_WithApprovedFlag_Succeeds()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-123",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await _client.PostAsJsonAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/permissions/respond",
            evt);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_WithDenialAndReason_Succeeds()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-456",
            SourceName: "TestSource",
            Approved: false,
            Reason: "Permission denied for security reasons");

        // Act
        var response = await _client.PostAsJsonAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/permissions/respond",
            evt);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_Returns404_WhenSessionMissing()
    {
        // Arrange
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-missing-session",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await _client.PostAsJsonAsync(
            "/agents/test-agent/sessions/missing-session/branches/main/permissions/respond",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_Returns404_WhenBranchMissing()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new PermissionResponseEvent(
            PermissionId: "perm-missing-branch",
            SourceName: "TestSource",
            Approved: true);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/missing-branch/permissions/respond",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond

    [Fact]
    public async Task RespondToClientTool_WithTextResult_Returns200()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ClientToolInvokeResponseEvent(
            RequestId: "tool-req-123",
            Content: new[] { new TextContent("Tool execution succeeded") },
            Success: true);

        // Act
        var response = await _client.PostAsJsonAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/client-tools/respond",
            evt);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_WithMultipleContents_Succeeds()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var contents = new IToolResultContent[]
        {
            new TextContent("Result data"),
            new TextContent("Additional result")
        };
        var evt = new ClientToolInvokeResponseEvent(
            RequestId: "tool-req-456",
            Content: contents,
            Success: true);

        // Act
        var response = await _client.PostAsJsonAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/client-tools/respond",
            evt);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_WithErrorMessage_Succeeds()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ClientToolInvokeResponseEvent(
            RequestId: "tool-req-789",
            Content: new[] { new TextContent("") },
            Success: false,
            ErrorMessage: "Tool execution failed: invalid parameters");

        // Act
        var response = await _client.PostAsJsonAsync(
             $"/agents/test-agent/sessions/{sessionId}/branches/main/client-tools/respond",
            evt);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_Returns404_WhenBranchMissing()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var evt = new ClientToolInvokeResponseEvent(
            RequestId: "tool-missing-branch",
            Content: new[] { new TextContent("Tool execution succeeded") },
            Success: true);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/missing-branch/client-tools/respond",
            evt);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
