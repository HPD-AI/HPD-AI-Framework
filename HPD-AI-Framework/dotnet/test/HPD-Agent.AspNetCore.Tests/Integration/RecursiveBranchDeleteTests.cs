using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
/// Factory with AllowRecursiveBranchDelete = true for testing the recursive delete feature.
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
                services.AddHPDAgent("test-agent", options =>
                {
                    options.SessionStorePath = Path.Combine(Path.GetTempPath(), $"hpd-recursive-{Guid.NewGuid()}");
                    options.AllowRecursiveBranchDelete = true;
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
/// Integration tests for recursive branch deletion via DELETE /branches/{bid}?recursive=true.
/// Split into two classes:
///   RecursiveBranchDeleteTests       — AllowRecursiveBranchDelete = true  (feature enabled)
///   RecursiveBranchDeleteGuardTests  — AllowRecursiveBranchDelete = false (default, feature disabled)
/// </summary>
public class RecursiveBranchDeleteTests : IClassFixture<RecursiveDeleteEnabledFactory>
{
    private readonly HttpClient _client;

    public RecursiveBranchDeleteTests(RecursiveDeleteEnabledFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private async Task<BranchDto> ForkBranch(string sessionId, string sourceBranchId, string newBranchId)
    {
        var request = new ForkBranchRequest(newBranchId, 0, null, null, null);
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/{sourceBranchId}/fork", request);
        response.IsSuccessStatusCode.Should().BeTrue($"fork to {newBranchId} should succeed");
        return (await response.Content.ReadFromJsonAsync<BranchDto>())!;
    }

    private async Task<BranchDto?> GetBranch(string sessionId, string branchId)
    {
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/{branchId}");
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<BranchDto>();
    }

    // ──────────────────────────────────────────────────────────────────
    // Core recursive delete behavior
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecursiveDelete_SingleChild_DeletesBothBranchAndChild()
    {
        // Arrange: main → fork-1 → fork-1a
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");

        // Act: delete fork-1 with recursive=true
        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        // Assert: 204, and both fork-1 and fork-1a are gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetBranch(sid, "fork-1")).Should().BeNull();
        (await GetBranch(sid, "fork-1a")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_ThreeLevelsDeep_DeletesAllDescendants()
    {
        // Arrange: main → fork-1 → fork-1a → fork-1a-i
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");
        await ForkBranch(sid, "fork-1a", "fork-1a-i");

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        // Assert: all three are gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetBranch(sid, "fork-1")).Should().BeNull();
        (await GetBranch(sid, "fork-1a")).Should().BeNull();
        (await GetBranch(sid, "fork-1a-i")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_MultipleChildren_DeletesAllChildren()
    {
        // Arrange: main → fork-1, and fork-1 has two children: fork-1a, fork-1b
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");
        await ForkBranch(sid, "fork-1", "fork-1b");

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        // Assert: fork-1, fork-1a, fork-1b all gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetBranch(sid, "fork-1")).Should().BeNull();
        (await GetBranch(sid, "fork-1a")).Should().BeNull();
        (await GetBranch(sid, "fork-1b")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_MixedDepthAndWidth_DeletesEntireSubtree()
    {
        // Arrange: main → fork-1, fork-1 → fork-1a + fork-1b, fork-1a → fork-1a-i
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");
        await ForkBranch(sid, "fork-1", "fork-1b");
        await ForkBranch(sid, "fork-1a", "fork-1a-i");

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        // Assert: entire subtree gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetBranch(sid, "fork-1")).Should().BeNull();
        (await GetBranch(sid, "fork-1a")).Should().BeNull();
        (await GetBranch(sid, "fork-1b")).Should().BeNull();
        (await GetBranch(sid, "fork-1a-i")).Should().BeNull();
        // main still exists
        (await GetBranch(sid, "main")).Should().NotBeNull();
    }

    [Fact]
    public async Task RecursiveDelete_SiblingsOfDeletedRoot_AreReindexedCorrectly()
    {
        // Arrange: main has two children fork-1 and fork-2 (siblings at same fork point)
        // fork-1 also has a child fork-1a
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "main", "fork-2");
        await ForkBranch(sid, "fork-1", "fork-1a");

        // Verify initial state: fork-1 and fork-2 are siblings of main (V3: group = [main, fork-1, fork-2] = 3 total)
        var beforeFork1 = await GetBranch(sid, "fork-1");
        var beforeFork2 = await GetBranch(sid, "fork-2");
        beforeFork1!.TotalSiblings.Should().Be(3);
        beforeFork2!.TotalSiblings.Should().Be(3);

        // Act: delete fork-1 recursively (also deletes fork-1a)
        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: group is now [main, fork-2] = 2 total; fork-1a gone
        var afterFork2 = await GetBranch(sid, "fork-2");
        afterFork2.Should().NotBeNull();
        afterFork2!.TotalSiblings.Should().Be(2);
        afterFork2.SiblingIndex.Should().Be(1);        // main=0, fork-2=1
        afterFork2.PreviousSiblingId.Should().Be("main");
        afterFork2.NextSiblingId.Should().BeNull();

        (await GetBranch(sid, "fork-1a")).Should().BeNull();
    }

    [Fact]
    public async Task RecursiveDelete_UpdatesParentChildBranchesList()
    {
        // Arrange: main → fork-1 → fork-1a
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");

        // Verify main has fork-1 as child
        var beforeMain = await GetBranch(sid, "main");
        beforeMain!.TotalForks.Should().Be(1);

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: main no longer has fork-1 in children
        var afterMain = await GetBranch(sid, "main");
        afterMain!.TotalForks.Should().Be(0);
    }

    [Fact]
    public async Task RecursiveDelete_LeafBranch_WorksNormally()
    {
        // A leaf branch with recursive=true should just delete that one branch
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");

        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetBranch(sid, "fork-1")).Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────
    // Guard: main branch always protected
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecursiveDelete_MainBranch_IsAlwaysRejected()
    {
        // Even with recursive=true and AllowRecursiveBranchDelete=true,
        // main is always protected
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a"); // give main a child subtree too

        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/main?recursive=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ToLowerInvariant().Should().Contain("main");
    }
}

/// <summary>
/// Guard tests: AllowRecursiveBranchDelete = false (the default).
/// Verifies that recursive=true is rejected when not opted in server-side.
/// Uses the shared TestWebApplicationFactory (default options, flag = false).
/// </summary>
public class RecursiveBranchDeleteGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RecursiveBranchDeleteGuardTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private async Task<BranchDto> ForkBranch(string sessionId, string sourceBranchId, string newBranchId)
    {
        var request = new ForkBranchRequest(newBranchId, 0, null, null, null);
        var response = await _client.PostAsJsonAsync(
            $"/sessions/{sessionId}/branches/{sourceBranchId}/fork", request);
        response.IsSuccessStatusCode.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<BranchDto>())!;
    }

    [Fact]
    public async Task RecursiveDelete_WithoutFlag_AndHasChildren_Returns400WithHasChildrenError()
    {
        // recursive=false (default), branch has children — existing rejection
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");

        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("HasChildren");
    }

    [Fact]
    public async Task RecursiveDelete_WithFlag_ButServerNotOptedIn_Returns400WithRecursiveDeleteDisabledError()
    {
        // recursive=true requested but AllowRecursiveBranchDelete=false on the server
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");
        await ForkBranch(sid, "fork-1", "fork-1a");

        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("RecursiveDeleteDisabled");
        // Branch must still exist
        var branch = await _client.GetAsync($"/sessions/{sid}/branches/fork-1");
        branch.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecursiveDelete_WithFlag_OnLeafBranch_SucceedsEvenWhenNotOptedIn()
    {
        // recursive=true on a leaf (no children) — guard only triggers when there are children
        var sid = await CreateSession();
        await ForkBranch(sid, "main", "fork-1");

        var response = await _client.DeleteAsync($"/sessions/{sid}/branches/fork-1?recursive=true");

        // No children → guard not triggered → deletes normally
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
