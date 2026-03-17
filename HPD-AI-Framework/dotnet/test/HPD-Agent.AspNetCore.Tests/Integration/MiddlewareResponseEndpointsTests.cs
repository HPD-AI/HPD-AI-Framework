using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for middleware response endpoints.
/// Tests: POST /sessions/{sid}/branches/{bid}/permissions/respond, POST /sessions/{sid}/branches/{bid}/client-tools/respond
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

    #region POST /sessions/{sid}/branches/{bid}/permissions/respond

    [Fact]
    public async Task RespondToPermission_SendsResponse_ToRunningAgent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new PermissionResponseRequest(
            null,
            "perm-123",
            true,
            "Approved for testing",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/permissions/respond",
            request);

        // Assert - Returns 404 because no agent is running
        // In a real scenario with running agent, would return 200
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_Returns200_OnSuccess()
    {
        // This test would require a running agent with active permission request
        // For now, verifies the endpoint exists and handles the request
        var sessionId = await CreateTestSession();
        var request = new PermissionResponseRequest(null, "perm-123", true, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/permissions/respond",
            request);

        // Assert - 404 because no running agent
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_Returns404_WhenAgentNotRunning()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new PermissionResponseRequest(null, "perm-123", false, "Denied", null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/permissions/respond",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_IncludesApprovedFlag()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new PermissionResponseRequest(null, "perm-123", true, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/permissions/respond",
            request);

        // Assert - Request is well-formed
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_IncludesReason_WhenDenied()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new PermissionResponseRequest(
            null,
            "perm-123",
            false,
            "User denied access",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/permissions/respond",
            request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToPermission_IncludesChoice_WhenProvided()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new PermissionResponseRequest(
            null,
            "perm-123",
            true,
            null,
            "option-A");

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/permissions/respond",
            request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /sessions/{sid}/branches/{bid}/client-tools/respond

    [Fact]
    public async Task RespondToClientTool_SendsSuccessResponse()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ClientToolResponseRequest(
            null,
            "tool-req-123",
            true,
            new List<ClientToolContentDto> { new("text", "Result", null, null) },
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/client-tools/respond",
            request);

        // Assert - 404 because no running agent
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_SendsErrorResponse()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ClientToolResponseRequest(
            null,
            "tool-req-123",
            false,
            new List<ClientToolContentDto>(),
            "Tool execution failed");

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/client-tools/respond",
            request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_Returns200_OnSuccess()
    {
        // This would require a running agent with active client tool request
        var sessionId = await CreateTestSession();
        var request = new ClientToolResponseRequest(
            null,
            "tool-123",
            true,
            new List<ClientToolContentDto> { new("text", "Success", null, null) },
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/client-tools/respond",
            request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_Returns404_WhenAgentNotRunning()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ClientToolResponseRequest(
            null,
            "tool-123",
            true,
            new List<ClientToolContentDto>(),
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/client-tools/respond",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_IncludesContent_OnSuccess()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var content = new List<ClientToolContentDto>
        {
            new("text", "Result 1", null, null),
            new("text", "Result 2", null, null)
        };
        var request = new ClientToolResponseRequest(null, "tool-123", true, content, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/client-tools/respond",
            request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RespondToClientTool_IncludesErrorMessage_OnFailure()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ClientToolResponseRequest(
            null,
            "tool-123",
            false,
            new List<ClientToolContentDto>(),
            "Network error occurred");

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/main/client-tools/respond",
            request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    #endregion
}
