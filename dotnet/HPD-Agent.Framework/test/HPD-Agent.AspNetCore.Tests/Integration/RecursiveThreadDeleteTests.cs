using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.AspNetCore;
using HPD.Agent.Hosting.Configuration;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Factory with AllowRecursiveThreadDelete = true for testing the recursive delete feature.
/// </summary>
public class RecursiveDeleteEnabledFactory : IDisposable
{
    private TestServer? _server;
    private HttpClient? _client;
    private readonly FakeChatClient _fakeChatClient = new();

    public HttpClient CreateClient()
    {
        if (_client != null) return _client;

        var contentRoot = Path.Combine(Path.GetTempPath(), $"hpd-recursive-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(contentRoot);

        var builder = new WebHostBuilder()
            .UseContentRoot(contentRoot)
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(_fakeChatClient);
                services.AddSingleton<IAgentFactory, TestWebApplicationAgentFactory>();
                services.AddTestApplicationCompositions().AddHPDAgent("test-agent", options =>
                {
                    options.SessionStorePath = Path.Combine(Path.GetTempPath(), $"hpd-recursive-{Guid.NewGuid()}");
                    options.AllowRecursiveThreadDelete = true;
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGroup("").MapHPDAgentApi("test-agent");
                });
            });

        _server = new TestServer(builder);
        _client = new HttpClient(_server.CreateHandler()) { BaseAddress = new Uri("http://localhost") };
        return _client;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _server?.Dispose();
    }
}

/// <summary>
/// Integration tests for recursive thread deletion via DELETE /threads/{bid}?recursive=true.
/// Split into two classes:
///   RecursiveThreadDeleteTests       — AllowRecursiveThreadDelete = true  (feature enabled)
///   RecursiveThreadDeleteGuardTests  — AllowRecursiveThreadDelete = false (default, feature disabled)
/// </summary>
public class RecursiveThreadDeleteTests : IClassFixture<RecursiveDeleteEnabledFactory>
{
    private readonly HttpClient _client;

    public RecursiveThreadDeleteTests(RecursiveDeleteEnabledFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private async Task<ThreadDto> ForkThread(string sessionId, string sourceThreadId, string newThreadId)
    {
        var forkMessageId = await EnsureForkMessageAsync(sessionId, sourceThreadId);
        var request = new ForkThreadRequest(newThreadId, forkMessageId, null, null, null);
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/{sourceThreadId}/fork", request);
        response.IsSuccessStatusCode.Should().BeTrue($"fork to {newThreadId} should succeed");
        return (await response.Content.ReadFromJsonAsync<ThreadDto>())!;
    }

    private async Task<string> EnsureForkMessageAsync(string sessionId, string threadId)
    {
        var existing = await TryGetFirstUserMessageIdAsync(sessionId, threadId);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing!;

        var inputResponse = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/{threadId}/inputs",
            new StreamTextRequest("Seed fork message"));
        inputResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var messageId = await TryGetFirstUserMessageIdAsync(
            sessionId,
            threadId,
            TimeSpan.FromSeconds(15),
            waitForRunCompletion: true);
        if (!string.IsNullOrWhiteSpace(messageId))
            return messageId!;

        throw new TimeoutException("Timed out waiting for a persisted fork message.");
    }

    private async Task<string?> TryGetFirstUserMessageIdAsync(
        string sessionId,
        string threadId,
        TimeSpan? timeout = null,
        bool waitForRunCompletion = false)
    {
        var events = await SseTestEventReader.ReadUntilAsync(
            _client,
            sessionId,
            threadId,
            observed => observed.OfType<UserMessageEvent>().Any() &&
                        (!waitForRunCompletion || observed.OfType<ThreadExecutionFinishedEvent>().Any()),
            timeout ?? TimeSpan.FromMilliseconds(150));
        return events.OfType<UserMessageEvent>().FirstOrDefault()?.MessageId;
    }

    private async Task<ThreadDto?> GetThread(string sessionId, string threadId)
    {
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/{threadId}");
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<ThreadDto>();
    }

    private async Task<ThreadDto> WaitForThread(
        string sessionId,
        string threadId,
        Func<ThreadDto, bool> predicate,
        string expectation)
    {
        for (var i = 0; i < 50; i++)
        {
            var thread = await GetThread(sessionId, threadId);
            if (thread != null && predicate(thread))
                return thread;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for thread '{threadId}' to satisfy: {expectation}.");
    }

    // ──────────────────────────────────────────────────────────────────
    // Core recursive delete behavior
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecursiveDelete_SingleChild_DeletesBothThreadAndChild()
    {
        // Arrange: main → fork-1 → fork-1a
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");

        // Act: delete fork-1 with recursive=true
        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        // Assert: 204, and both fork-1 and fork-1a are gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetThread(sid, "fork-1")).Should().BeNull();
        (await GetThread(sid, "fork-1a")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_ThreeLevelsDeep_DeletesAllDescendants()
    {
        // Arrange: main → fork-1 → fork-1a → fork-1a-i
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");
        await ForkThread(sid, "fork-1a", "fork-1a-i");

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        // Assert: all three are gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetThread(sid, "fork-1")).Should().BeNull();
        (await GetThread(sid, "fork-1a")).Should().BeNull();
        (await GetThread(sid, "fork-1a-i")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_MultipleChildren_DeletesAllChildren()
    {
        // Arrange: main → fork-1, and fork-1 has two children: fork-1a, fork-1b
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");
        await ForkThread(sid, "fork-1", "fork-1b");

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        // Assert: fork-1, fork-1a, fork-1b all gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetThread(sid, "fork-1")).Should().BeNull();
        (await GetThread(sid, "fork-1a")).Should().BeNull();
        (await GetThread(sid, "fork-1b")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_MixedDepthAndWidth_DeletesEntireSubtree()
    {
        // Arrange: main → fork-1, fork-1 → fork-1a + fork-1b, fork-1a → fork-1a-i
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");
        await ForkThread(sid, "fork-1", "fork-1b");
        await ForkThread(sid, "fork-1a", "fork-1a-i");

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        // Assert: entire subtree gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetThread(sid, "fork-1")).Should().BeNull();
        (await GetThread(sid, "fork-1a")).Should().BeNull();
        (await GetThread(sid, "fork-1b")).Should().BeNull();
        (await GetThread(sid, "fork-1a-i")).Should().BeNull();
        // main still exists
        (await GetThread(sid, "main")).Should().NotBeNull();
    }

    [Fact]
    public async Task RecursiveDelete_RemovesDeletedRootFromForkGroupLineage()
    {
        // Arrange: main has two forks at the same fork point, and fork-1 has a child.
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "main", "fork-2");
        await ForkThread(sid, "fork-1", "fork-1a");

        var beforeFork1 = await GetThread(sid, "fork-1");
        var beforeFork2 = await GetThread(sid, "fork-2");
        beforeFork1!.ForkedFrom.Should().Be("main");
        beforeFork2!.ForkedFrom.Should().Be("main");
        beforeFork2.ForkedAtMessageId.Should().Be(beforeFork1.ForkedAtMessageId);

        // Act: delete fork-1 recursively (also deletes fork-1a)
        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterFork2 = await GetThread(sid, "fork-2");
        afterFork2.Should().NotBeNull();
        afterFork2!.ForkedFrom.Should().Be("main");
        afterFork2.ForkedAtMessageId.Should().Be(beforeFork2.ForkedAtMessageId);

        (await GetThread(sid, "fork-1a")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_UpdatesParentChildThreadsList()
    {
        // Arrange: main → fork-1 → fork-1a
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");

        // Verify main has fork-1 as child
        var beforeMain = await WaitForThread(
            sid,
            "main",
            static thread => thread.TotalForks == 1,
            "TotalForks == 1");
        beforeMain.TotalForks.Should().Be(1);

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: main no longer has fork-1 in children
        var afterMain = await WaitForThread(
            sid,
            "main",
            static thread => thread.TotalForks == 0,
            "TotalForks == 0");
        afterMain.TotalForks.Should().Be(0);
    }

    [Fact]
    public async Task RecursiveDelete_LeafThread_WorksNormally()
    {
        // A leaf thread with recursive=true should just delete that one thread
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");

        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetThread(sid, "fork-1")).Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────
    // Guard: main thread always protected
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecursiveDelete_MainThread_IsAlwaysRejected()
    {
        // Even with recursive=true and AllowRecursiveThreadDelete=true,
        // main is always protected
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a"); // give main a child subtree too

        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/main?recursive=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ToLowerInvariant().Should().Contain("main");
    }
}

/// <summary>
/// Guard tests: AllowRecursiveThreadDelete = false (the default).
/// Verifies that recursive=true is rejected when not opted in server-side.
/// Uses the shared TestWebApplicationFactory (default options, flag = false).
/// </summary>
public class RecursiveThreadDeleteGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RecursiveThreadDeleteGuardTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private async Task<ThreadDto> ForkThread(string sessionId, string sourceThreadId, string newThreadId)
    {
        var forkMessageId = await EnsureForkMessageAsync(sessionId, sourceThreadId);
        var request = new ForkThreadRequest(newThreadId, forkMessageId, null, null, null);
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/{sourceThreadId}/fork", request);
        response.IsSuccessStatusCode.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ThreadDto>())!;
    }

    private async Task<string> EnsureForkMessageAsync(string sessionId, string threadId)
    {
        var existing = await TryGetFirstUserMessageIdAsync(sessionId, threadId);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing!;

        var inputResponse = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/{threadId}/inputs",
            new StreamTextRequest("Seed fork message"));
        inputResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var messageId = await TryGetFirstUserMessageIdAsync(
            sessionId,
            threadId,
            TimeSpan.FromSeconds(15));
        if (!string.IsNullOrWhiteSpace(messageId))
            return messageId!;

        throw new TimeoutException("Timed out waiting for a persisted fork message.");
    }

    private async Task<string?> TryGetFirstUserMessageIdAsync(
        string sessionId,
        string threadId,
        TimeSpan? timeout = null)
    {
        var events = await SseTestEventReader.ReadUntilAsync(
            _client,
            sessionId,
            threadId,
            static observed => observed.OfType<UserMessageEvent>().Any(),
            timeout ?? TimeSpan.FromMilliseconds(150));
        return events.OfType<UserMessageEvent>().FirstOrDefault()?.MessageId;
    }

    [Fact]
    public async Task RecursiveDelete_WithoutFlag_AndHasChildren_Returns400WithHasChildrenError()
    {
        // recursive=false (default), thread has children — existing rejection
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");

        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("HasChildren");
    }

    [Fact]
    public async Task RecursiveDelete_WithFlag_ButServerNotOptedIn_Returns400WithRecursiveDeleteDisabledError()
    {
        // recursive=true requested but AllowRecursiveThreadDelete=false on the server
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");
        await ForkThread(sid, "fork-1", "fork-1a");

        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RecursiveDeleteDisabled");
        // Thread must still exist
        var thread = await _client.GetAsync($"/sessions/{sid}/threads/fork-1");
        thread.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecursiveDelete_WithFlag_OnLeafThread_SucceedsEvenWhenNotOptedIn()
    {
        // recursive=true on a leaf (no children) — guard only triggers when there are children
        var sid = await CreateSession();
        await ForkThread(sid, "main", "fork-1");

        var response = await _client.DeleteAsync($"/sessions/{sid}/threads/fork-1?recursive=true");

        // No children → guard not triggered → deletes normally
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
