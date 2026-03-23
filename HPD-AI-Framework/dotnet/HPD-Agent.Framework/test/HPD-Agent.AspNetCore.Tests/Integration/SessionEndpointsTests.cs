using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for Session CRUD endpoints.
/// Tests: POST /sessions, GET /sessions/{id}, PATCH /sessions/{id}, DELETE /sessions/{id}, POST /sessions/search
/// </summary>
public class SessionEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SessionEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region POST /sessions

    [Fact]
    public async Task CreateSession_Returns201_WithSessionDto()
    {
        // Act
        var response = await _client.PostAsync("/sessions", null);

        // Debug: Check response
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"POST /sessions failed with {response.StatusCode}. Body: {errorBody}");
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session.Should().NotBeNull();
        session!.Id.Should().NotBeNullOrEmpty();
        session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        session.LastActivity.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateSession_CreatesMainBranch_Automatically()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act - List branches
        var branchesResponse = await _client.GetAsync($"/sessions/{session!.Id}/branches");

        // Debug: Check response status and content
        if (!branchesResponse.IsSuccessStatusCode)
        {
            var errorBody = await branchesResponse.Content.ReadAsStringAsync();
            throw new Exception($"GET /sessions/{session.Id}/branches failed with {branchesResponse.StatusCode}. Body: {errorBody}");
        }

        var branches = await branchesResponse.Content.ReadFromJsonAsync<List<BranchDto>>();

        // Assert
        branches.Should().NotBeNull();
        branches!.Should().ContainSingle();
        branches[0].Id.Should().Be("main");
    }

    [Fact]
    public async Task CreateSession_AcceptsCustomSessionId()
    {
        // Arrange
        var request = new CreateSessionRequest("custom-session-123", null);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Id.Should().Be("custom-session-123");
    }

    [Fact]
    public async Task CreateSession_AcceptsMetadata()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["project"] = "test",
            ["user"] = "alice",
            ["priority"] = 5
        };
        var request = new CreateSessionRequest(null, metadata);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Metadata.Should().NotBeNull();
        session.Metadata!.Should().ContainKey("project");
        session.Metadata.Should().ContainKey("user");
    }

    [Fact]
    public async Task CreateSession_GeneratesSessionId_WhenNotProvided()
    {
        // Act
        var response = await _client.PostAsync("/sessions", null);

        // Assert
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Id.Should().NotBeNullOrEmpty();
        session.Id.Should().MatchRegex("^[a-zA-Z0-9_-]+$");
    }

    #endregion

    #region POST /sessions/search

    [Fact]
    public async Task SearchSessions_ReturnsAllSessions_WithNoFilters()
    {
        // Arrange - Create 3 sessions
        await _client.PostAsync("/sessions", null);
        await _client.PostAsync("/sessions", null);
        await _client.PostAsync("/sessions", null);

        var request = new SearchSessionsRequest(null, 0, 100);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchSessions_FiltersByMetadata_Correctly()
    {
        // Arrange - Create sessions with different metadata
        var metadata1 = new Dictionary<string, object> { ["project"] = "projectA" };
        var metadata2 = new Dictionary<string, object> { ["project"] = "projectB" };

        await _client.PostAsJsonAsync("/sessions", new CreateSessionRequest(null, metadata1));
        await _client.PostAsJsonAsync("/sessions", new CreateSessionRequest(null, metadata2));

        var searchRequest = new SearchSessionsRequest(
            new Dictionary<string, object> { ["project"] = "projectA" },
            0,
            100);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions/search", searchRequest);

        // Assert
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().AllSatisfy(s =>
            s.Metadata.Should().ContainKey("project"));
    }

    [Fact]
    public async Task SearchSessions_SupportsOffset_ForPagination()
    {
        // Arrange - Create multiple sessions
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsync("/sessions", null);
        }

        var request = new SearchSessionsRequest(null, 2, 10);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionDto>>();
        sessions.Should().NotBeNull();
        // Offset of 2 means we skip the first 2 sessions
    }

    [Fact]
    public async Task SearchSessions_SupportsLimit_ForPagination()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _client.PostAsync("/sessions", null);
        }

        var request = new SearchSessionsRequest(null, 0, 3);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions/search", request);

        // Assert
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchSessions_ReturnsEmptyArray_WhenNoMatches()
    {
        // Arrange
        var request = new SearchSessionsRequest(
            new Dictionary<string, object> { ["nonexistent"] = "value" },
            0,
            100);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions/search", request);

        // Assert
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().BeEmpty();
    }

    #endregion

    #region GET /sessions/{sessionId}

    [Fact]
    public async Task GetSession_Returns200_WithSessionDto()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var createdSession = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act
        var response = await _client.GetAsync($"/sessions/{createdSession!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Id.Should().Be(createdSession.Id);
    }

    [Fact]
    public async Task GetSession_Returns404_WhenNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent-session");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region PATCH /sessions/{sessionId}

    [Fact]
    public async Task UpdateSession_MergesMetadata_Correctly()
    {
        // Arrange - Create session with initial metadata
        var initialMetadata = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };
        var createResponse = await _client.PostAsJsonAsync("/sessions",
            new CreateSessionRequest(null, initialMetadata));
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act - Update with new metadata
        var updateMetadata = new Dictionary<string, object?>
        {
            ["key2"] = "updated_value2", // Update existing
            ["key3"] = "value3"           // Add new
        };
        var updateRequest = new UpdateSessionRequest(updateMetadata);
        var updateResponse = await _client.PatchAsJsonAsync(
            $"/sessions/{session!.Id}",
            updateRequest);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SessionDto>();
        updated!.Metadata.Should().ContainKey("key1"); // Preserved
        updated.Metadata!["key2"].ToString().Should().Be("updated_value2"); // Updated
        updated.Metadata.Should().ContainKey("key3"); // Added
    }

    [Fact]
    public async Task UpdateSession_RemovesKey_WhenSetToNull()
    {
        // Arrange
        var initialMetadata = new Dictionary<string, object> { ["removeMe"] = "value" };
        var createResponse = await _client.PostAsJsonAsync("/sessions",
            new CreateSessionRequest(null, initialMetadata));
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act
        var updateRequest = new UpdateSessionRequest(
            new Dictionary<string, object?> { ["removeMe"] = null });
        var updateResponse = await _client.PatchAsJsonAsync(
            $"/sessions/{session!.Id}",
            updateRequest);

        // Assert
        var updated = await updateResponse.Content.ReadFromJsonAsync<SessionDto>();
        updated!.Metadata.Should().NotContainKey("removeMe");
    }

    [Fact]
    public async Task UpdateSession_PreservesUnmentionedKeys()
    {
        // Arrange
        var initialMetadata = new Dictionary<string, object>
        {
            ["keep1"] = "value1",
            ["keep2"] = "value2"
        };
        var createResponse = await _client.PostAsJsonAsync("/sessions",
            new CreateSessionRequest(null, initialMetadata));
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act - Update without mentioning existing keys
        var updateRequest = new UpdateSessionRequest(
            new Dictionary<string, object?> { ["new"] = "newValue" });
        var updateResponse = await _client.PatchAsJsonAsync(
            $"/sessions/{session!.Id}",
            updateRequest);

        // Assert
        var updated = await updateResponse.Content.ReadFromJsonAsync<SessionDto>();
        updated!.Metadata.Should().ContainKey("keep1");
        updated.Metadata.Should().ContainKey("keep2");
        updated.Metadata.Should().ContainKey("new");
    }

    [Fact]
    public async Task UpdateSession_UpdatesLastActivity()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();
        var originalLastActivity = session!.LastActivity;

        await Task.Delay(100); // Ensure time difference

        // Act
        var updateRequest = new UpdateSessionRequest(
            new Dictionary<string, object?> { ["updated"] = "yes" });
        var updateResponse = await _client.PatchAsJsonAsync(
            $"/sessions/{session.Id}",
            updateRequest);

        // Assert
        var updated = await updateResponse.Content.ReadFromJsonAsync<SessionDto>();
        updated!.LastActivity.Should().BeAfter(originalLastActivity);
    }

    [Fact]
    public async Task UpdateSession_Returns404_WhenNotFound()
    {
        // Arrange
        var updateRequest = new UpdateSessionRequest(
            new Dictionary<string, object?> { ["key"] = "value" });

        // Act
        var response = await _client.PatchAsJsonAsync(
            "/sessions/nonexistent",
            updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DELETE /sessions/{sessionId}

    [Fact]
    public async Task DeleteSession_Returns204_OnSuccess()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/sessions/{session!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteSession_DeletesAllBranches()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act
        await _client.DeleteAsync($"/sessions/{session!.Id}");

        // Assert - Try to get branches (should fail because session is gone)
        var branchesResponse = await _client.GetAsync($"/sessions/{session.Id}/branches");
        branchesResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSession_Returns404_WhenNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/sessions/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSession_RemovesAgentFromCache()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act
        await _client.DeleteAsync($"/sessions/{session!.Id}");

        // Assert - Subsequent operations should fail
        var getResponse = await _client.GetAsync($"/sessions/{session.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
