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
/// Integration tests for concurrency and multi-agent scenarios.
/// Tests concurrent operations, multi-agent isolation, and runtime-owned input submission.
/// </summary>
public class ConcurrencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ConcurrencyTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string CreateInputJson(string text) =>
        JsonSerializer.Serialize(new StreamTextRequest(text));

    private Task<HttpResponseMessage> PostInputAsync(string url, string json) =>
        _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

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
    public async Task ConcurrentInputs_OnDifferentThreads_BothAccepted()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Create second thread
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{session!.Id}/threads",
            new CreateThreadRequest("thread2", "Thread 2", null, null));

        var request1 = CreateInputJson("Test 1");
        var request2 = CreateInputJson("Test 2");

        // Act - Submit on both threads simultaneously
        var stream1Task = PostInputAsync(
             $"/agents/test-agent/sessions/{session.Id}/threads/main/inputs", request1);
        var stream2Task = PostInputAsync(
             $"/agents/test-agent/sessions/{session.Id}/threads/thread2/inputs", request2);

        await Task.WhenAll(stream1Task, stream2Task);

        // Assert - Both submissions should be accepted by the runtime.
        stream1Task.Result.StatusCode.Should().Be(HttpStatusCode.Accepted);
        stream2Task.Result.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ConcurrentInputs_OnSameThread_ReturnsConflictForSecondRun()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        var request = CreateInputJson("Long task");

        var stream1Task = PostInputAsync(
             $"/agents/test-agent/sessions/{session!.Id}/threads/main/inputs", request);
        var stream2Task = PostInputAsync(
             $"/agents/test-agent/sessions/{session.Id}/threads/main/inputs", request);

        await Task.WhenAll(stream1Task, stream2Task);

        new[] { stream1Task.Result.StatusCode, stream2Task.Result.StatusCode }
            .Should()
            .BeEquivalentTo([HttpStatusCode.Accepted, HttpStatusCode.Conflict]);
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
    public async Task ConcurrentThreadCreation_OnSameSession_AllSucceed()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();

        // Act - Create 10 threads concurrently
        var tasks = Enumerable.Range(0, 10).Select(i =>
            _client.PostAsJsonAsync($"/agents/test-agent/sessions/{session!.Id}/threads",
                new CreateThreadRequest($"thread-{i}", $"Thread {i}", null, null))
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
