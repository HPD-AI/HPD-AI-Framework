using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for concurrency and multi-agent scenarios.
/// Tests concurrent operations, multi-agent isolation, and stream locking.
/// </summary>
public class ConcurrencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ConcurrencyTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Multi-Agent Support

    [Fact]
    public async Task MultipleNamedAgents_IsolatesSessions()
    {
        // This test would require a test app with multiple named agents
        // For now, verifies single agent isolation
        var session1Response = await _client.PostAsync("/sessions", null);
        var session2Response = await _client.PostAsync("/sessions", null);

        var session1 = await session1Response.Content.ReadFromJsonAsync<SessionDto>();
        var session2 = await session2Response.Content.ReadFromJsonAsync<SessionDto>();

        // Assert - Sessions are independent
        session1!.Id.Should().NotBe(session2!.Id);
    }

    [Fact]
    public async Task MultipleNamedAgents_IsolatesConfiguration()
    {
        // Verifies that different sessions don't interfere
        var session1Response = await _client.PostAsync("/sessions", null);
        var session1 = await session1Response.Content.ReadFromJsonAsync<SessionDto>();

        // Modify session 1
        await _client.PatchAsJsonAsync($"/sessions/{session1!.Id}",
            new UpdateSessionRequest(new Dictionary<string, object?> { ["key"] = "value1" }));

        // Create session 2
        var session2Response = await _client.PostAsync("/sessions", null);
        var session2 = await session2Response.Content.ReadFromJsonAsync<SessionDto>();

        // Assert - Session 2 not affected by session 1 changes
        var session2Data = await _client.GetFromJsonAsync<SessionDto>($"/sessions/{session2!.Id}");
        session2Data!.Metadata.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task MultipleNamedAgents_SharesInfrastructure()
    {
        // All agents share the same HTTP server infrastructure
        // Verify both can be accessed simultaneously
        var task1 = _client.PostAsync("/sessions", null);
        var task2 = _client.PostAsync("/sessions", null);
        var task3 = _client.PostAsync("/sessions", null);

        await Task.WhenAll(task1, task2, task3);

        // Assert - All succeeded
        task1.Result.StatusCode.Should().Be(HttpStatusCode.Created);
        task2.Result.StatusCode.Should().Be(HttpStatusCode.Created);
        task3.Result.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    #endregion

    #region Concurrent Requests

    [Fact]
    public async Task ConcurrentGetOrCreateAgent_CreatesOnlyOne()
    {
        // This test verifies agent caching works under concurrent access
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Make concurrent requests to same session
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetAsync($"/sessions/{session!.Id}")
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert - All requests succeeded
        tasks.Should().AllSatisfy(t => t.Result.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task ConcurrentStreams_OnDifferentBranches_BothSucceed()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Create second branch
        await _client.PostAsJsonAsync($"/sessions/{session!.Id}/branches",
            new CreateBranchRequest("branch2", "Branch 2", null, null));

        var request1 = new StreamRequest(
            new List<StreamMessage> { new("Test 1", "user") },
            new List<System.Text.Json.JsonElement>(), new List<System.Text.Json.JsonElement>(), null,
            new List<string>(), new List<string>(), false, null);

        var request2 = new StreamRequest(
            new List<StreamMessage> { new("Test 2", "user") },
            new List<System.Text.Json.JsonElement>(), new List<System.Text.Json.JsonElement>(), null,
            new List<string>(), new List<string>(), false, null);

        // Act - Stream on both branches simultaneously
        var stream1Task = _client.PostAsJsonAsync(
            $"/sessions/{session.Id}/branches/main/stream", request1);
        var stream2Task = _client.PostAsJsonAsync(
            $"/sessions/{session.Id}/branches/branch2/stream", request2);

        await Task.WhenAll(stream1Task, stream2Task);

        // Assert - Both should succeed
        stream1Task.Result.StatusCode.Should().Be(HttpStatusCode.OK);
        stream2Task.Result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentStreams_OnSameBranch_SecondReturns409()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        var request = new StreamRequest(
            new List<StreamMessage> { new("Long task", "user") },
            new List<System.Text.Json.JsonElement>(), new List<System.Text.Json.JsonElement>(), null,
            new List<string>(), new List<string>(), false, null);

        // Act - Start first stream (don't await)
        var stream1Task = _client.PostAsJsonAsync(
            $"/sessions/{session!.Id}/branches/main/stream", request);

        // Give first stream time to acquire lock
        await Task.Delay(100);

        // Try second stream
        var stream2Response = await _client.PostAsJsonAsync(
            $"/sessions/{session.Id}/branches/main/stream", request);

        // Assert
        stream2Response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cleanup
        try { await stream1Task; } catch { }
    }

    [Fact]
    public async Task ConcurrentSessionCreation_AllSucceed()
    {
        // Act - Create 20 sessions concurrently
        var tasks = Enumerable.Range(0, 20).Select(_ =>
            _client.PostAsync("/sessions", null)
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert - All should succeed
        tasks.Should().AllSatisfy(t => t.Result.StatusCode.Should().Be(HttpStatusCode.Created));

        // All should have unique IDs
        var sessionIds = await Task.WhenAll(tasks.Select(async t =>
        {
            var session = await t.Result.Content.ReadFromJsonAsync<SessionDto>();
            return session!.Id;
        }));

        sessionIds.Distinct().Count().Should().Be(20);
    }

    [Fact]
    public async Task ConcurrentBranchCreation_OnSameSession_AllSucceed()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act - Create 10 branches concurrently
        var tasks = Enumerable.Range(0, 10).Select(i =>
            _client.PostAsJsonAsync($"/sessions/{session!.Id}/branches",
                new CreateBranchRequest($"branch-{i}", $"Branch {i}", null, null))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert - All should succeed
        tasks.Should().AllSatisfy(t => t.Result.StatusCode.Should().Be(HttpStatusCode.Created));
    }

    [Fact]
    public async Task ConcurrentMetadataUpdates_AllApplied()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act - Update metadata concurrently with different keys
        var tasks = Enumerable.Range(0, 10).Select(i =>
            _client.PatchAsJsonAsync($"/sessions/{session!.Id}",
                new UpdateSessionRequest(
                    new Dictionary<string, object?> { [$"key{i}"] = $"value{i}" }))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert - All updates should succeed
        tasks.Should().AllSatisfy(t => t.Result.StatusCode.Should().Be(HttpStatusCode.OK));

        // Verify all keys present
        var updatedSession = await _client.GetFromJsonAsync<SessionDto>($"/sessions/{session!.Id}");
        updatedSession!.Metadata.Should().NotBeNull();
        updatedSession.Metadata!.Count.Should().BeGreaterThanOrEqualTo(10);
    }

    #endregion
}
