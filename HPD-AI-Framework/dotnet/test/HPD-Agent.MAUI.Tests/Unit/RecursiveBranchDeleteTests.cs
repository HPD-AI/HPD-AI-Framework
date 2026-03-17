using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Maui;
using HPD.Agent.Maui.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Maui;
using Moq;

namespace HPD.Agent.Maui.Tests.Unit;

/// <summary>
/// Unit tests for recursive branch deletion in HybridWebViewAgentProxy.
/// Tests the recursive=true path, the AllowRecursiveBranchDelete gate,
/// and that the main branch guard still applies.
///
/// Two fixture helpers:
///   MakeProxy(allowRecursive: false) — default, safe config
///   MakeProxy(allowRecursive: true)  — opt-in config for recursive tests
/// </summary>
public class RecursiveBranchDeleteTests : IDisposable
{
    private readonly InMemorySessionStore _store;
    private readonly HybridWebViewAgentProxy _proxy;
    private readonly HybridWebViewAgentProxy _recursiveProxy;

    public RecursiveBranchDeleteTests()
    {
        _store = new InMemorySessionStore();
        _proxy = MakeProxy(_store, allowRecursive: false);
        _recursiveProxy = MakeProxy(_store, allowRecursive: true);
    }

    public void Dispose()
    {
        // Nothing to dispose — store is in-memory
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    private static HybridWebViewAgentProxy MakeProxy(InMemorySessionStore store, bool allowRecursive)
    {
        var mockWebView = new Mock<IHybridWebView>();
        var optionsMonitor = new OptionsMonitorWrapper(allowRecursive);
        optionsMonitor.CurrentValue.AgentConfig = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        };
        optionsMonitor.CurrentValue.ConfigureAgent = builder =>
        {
            var chatClient = new FakeChatClient();
            var registry = new TestProviderRegistry(chatClient);
            var field = typeof(AgentBuilder).GetField("_providerRegistry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(builder, registry);
        };
        var manager = new MauiSessionManager(store, optionsMonitor, Options.DefaultName);
        var agentManager = new MauiAgentManager(new InMemoryAgentStore(), manager, optionsMonitor, Options.DefaultName);
        return new TestProxy(manager, agentManager, mockWebView.Object);
    }

    private async Task<string> CreateSession(HybridWebViewAgentProxy proxy)
    {
        var json = await proxy.CreateSession();
        var dto = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(json);
        return dto!.Id;
    }

    private async Task ForkBranch(HybridWebViewAgentProxy proxy, string sid, string sourceBid, string newBid)
    {
        var request = new ForkBranchRequest(newBid, 0, null, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        await proxy.ForkBranch(sid, sourceBid, json);
    }

    private async Task<bool> BranchExists(HybridWebViewAgentProxy proxy, string sid, string bid)
    {
        try
        {
            await proxy.GetBranch(sid, bid);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Guard: non-recursive (default) still rejects branches with children
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBranch_WithChildren_NonRecursive_Throws()
    {
        var sid = await CreateSession(_proxy);
        await ForkBranch(_proxy, sid, "main", "fork-1");
        await ForkBranch(_proxy, sid, "fork-1", "fork-1a");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _proxy.DeleteBranch(sid, "fork-1"));

        ex.Message.ToLowerInvariant().Should().Contain("child");
        (await BranchExists(_proxy, sid, "fork-1")).Should().BeTrue();
        (await BranchExists(_proxy, sid, "fork-1a")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBranch_WithChildren_RecursiveRequested_ButNotEnabled_Throws()
    {
        var sid = await CreateSession(_proxy);
        await ForkBranch(_proxy, sid, "main", "fork-1");
        await ForkBranch(_proxy, sid, "fork-1", "fork-1a");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _proxy.DeleteBranch(sid, "fork-1", recursive: true));

        ex.Message.Should().Contain("AllowRecursiveBranchDelete");
        // Branch still intact
        (await BranchExists(_proxy, sid, "fork-1")).Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    // Core recursive delete behavior (AllowRecursiveBranchDelete = true)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBranch_Recursive_SingleChild_DeletesBothBranchAndChild()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");
        await ForkBranch(_recursiveProxy, sid, "fork-1", "fork-1a");

        await _recursiveProxy.DeleteBranch(sid, "fork-1", recursive: true);

        (await BranchExists(_recursiveProxy, sid, "fork-1")).Should().BeFalse();
        (await BranchExists(_recursiveProxy, sid, "fork-1a")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBranch_Recursive_ThreeLevels_DeletesAllDescendants()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");
        await ForkBranch(_recursiveProxy, sid, "fork-1", "fork-1a");
        await ForkBranch(_recursiveProxy, sid, "fork-1a", "fork-1a-i");

        await _recursiveProxy.DeleteBranch(sid, "fork-1", recursive: true);

        (await BranchExists(_recursiveProxy, sid, "fork-1")).Should().BeFalse();
        (await BranchExists(_recursiveProxy, sid, "fork-1a")).Should().BeFalse();
        (await BranchExists(_recursiveProxy, sid, "fork-1a-i")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBranch_Recursive_MultipleChildren_DeletesAll()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");
        await ForkBranch(_recursiveProxy, sid, "fork-1", "fork-1a");
        await ForkBranch(_recursiveProxy, sid, "fork-1", "fork-1b");

        await _recursiveProxy.DeleteBranch(sid, "fork-1", recursive: true);

        (await BranchExists(_recursiveProxy, sid, "fork-1")).Should().BeFalse();
        (await BranchExists(_recursiveProxy, sid, "fork-1a")).Should().BeFalse();
        (await BranchExists(_recursiveProxy, sid, "fork-1b")).Should().BeFalse();
        // main is untouched
        (await BranchExists(_recursiveProxy, sid, "main")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBranch_Recursive_UpdatesParentChildBranchesList()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");
        await ForkBranch(_recursiveProxy, sid, "fork-1", "fork-1a");

        // Verify main has fork-1 as child before
        var beforeJson = await _recursiveProxy.GetBranch(sid, "main");
        var before = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(beforeJson);
        before!.TotalForks.Should().Be(1);

        await _recursiveProxy.DeleteBranch(sid, "fork-1", recursive: true);

        var afterJson = await _recursiveProxy.GetBranch(sid, "main");
        var after = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(afterJson);
        after!.TotalForks.Should().Be(0);
    }

    [Fact]
    public async Task DeleteBranch_Recursive_SiblingsOfDeletedRoot_AreReindexed()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");
        await ForkBranch(_recursiveProxy, sid, "main", "fork-2");
        await ForkBranch(_recursiveProxy, sid, "fork-1", "fork-1a");

        await _recursiveProxy.DeleteBranch(sid, "fork-1", recursive: true);

        var fork2Json = await _recursiveProxy.GetBranch(sid, "fork-2");
        var fork2 = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(fork2Json);
        fork2!.TotalSiblings.Should().Be(1);
        fork2.SiblingIndex.Should().Be(0);
        fork2.PreviousSiblingId.Should().BeNull();
        fork2.NextSiblingId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteBranch_Recursive_LeafBranch_WorksNormally()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");

        // recursive=true on a leaf should just delete it normally
        await _recursiveProxy.DeleteBranch(sid, "fork-1", recursive: true);

        (await BranchExists(_recursiveProxy, sid, "fork-1")).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // Guard: main always protected regardless of recursive flag
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBranch_Recursive_MainBranch_AlwaysThrows()
    {
        var sid = await CreateSession(_recursiveProxy);
        await ForkBranch(_recursiveProxy, sid, "main", "fork-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _recursiveProxy.DeleteBranch(sid, "main", recursive: true));

        ex.Message.ToLowerInvariant().Should().Contain("main");
    }

    // ──────────────────────────────────────────────────────────────────
    // Test infrastructure
    // ──────────────────────────────────────────────────────────────────

    private class TestProxy(MauiSessionManager manager, MauiAgentManager agentManager, IHybridWebView webView)
        : HybridWebViewAgentProxy(manager, agentManager, webView);

    private class OptionsMonitorWrapper(bool allowRecursive = false) : IOptionsMonitor<HPDAgentConfig>
    {
        public HPDAgentConfig CurrentValue { get; } = new HPDAgentConfig
        {
            AllowRecursiveBranchDelete = allowRecursive
        };
        public HPDAgentConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentConfig, string?> listener) => null;
    }
}
