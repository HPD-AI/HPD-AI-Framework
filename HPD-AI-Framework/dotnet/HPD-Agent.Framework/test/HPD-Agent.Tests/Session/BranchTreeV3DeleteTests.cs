using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// V3 Branch Tree Architecture - Delete Operation Tests
/// Tests for safe deletion with referential integrity and sibling reindexing.
/// </summary>
public class BranchTreeV3DeleteTests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // P0 CRITICAL - DELETE SAFETY
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test15_DeleteBranch_WithChildren_ThrowsError()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create fork-1, then fork from fork-1 (so fork-1 has children)
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        fork1.Session = session;
        fork1.AddMessage(AssistantMessage("Response"));
        await store.SaveBranchAsync("test-session", fork1);

        var fork2 = await agent.ForkBranchAsync(fork1, "fork-2", fromMessageIndex: 0);

        // Act & Assert - Cannot delete fork-1 because it has children
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await agent.DeleteBranchAsync("test-session", "fork-1"));

        Assert.Contains("child", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test17_DeleteBranch_ReindexesRemainingSiblings()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create 3 siblings
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        var fork2 = await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        var fork3 = await agent.ForkBranchAsync(main, "fork-3", fromMessageIndex: 0);

        // Initial state: main(0), fork-1(1), fork-2(2), fork-3(3) — TotalSiblings=4
        var beforeMain = await store.LoadBranchAsync("test-session", "main");
        var beforeFork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var beforeFork2 = await store.LoadBranchAsync("test-session", "fork-2");
        var beforeFork3 = await store.LoadBranchAsync("test-session", "fork-3");
        Assert.Equal(0, beforeMain!.SiblingIndex);
        Assert.Equal(1, beforeFork1!.SiblingIndex);
        Assert.Equal(2, beforeFork2!.SiblingIndex);
        Assert.Equal(3, beforeFork3!.SiblingIndex);

        // Act - Delete middle sibling (fork-2)
        await agent.DeleteBranchAsync("test-session", "fork-2");

        // Assert - Remaining: main(0), fork-1(1), fork-3(2) — TotalSiblings=3
        var afterMain = await store.LoadBranchAsync("test-session", "main");
        var afterFork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var afterFork2 = await store.LoadBranchAsync("test-session", "fork-2");
        var afterFork3 = await store.LoadBranchAsync("test-session", "fork-3");

        Assert.Null(afterFork2); // Deleted
        Assert.Equal(0, afterMain!.SiblingIndex);
        Assert.Equal(1, afterFork1!.SiblingIndex);
        Assert.Equal(2, afterFork3!.SiblingIndex); // Shifted down from 3 to 2
        Assert.Equal(3, afterMain.TotalSiblings);
        Assert.Equal(3, afterFork1.TotalSiblings);
        Assert.Equal(3, afterFork3.TotalSiblings);
    }

    [Fact]
    public async Task Test18_DeleteBranch_UpdatesNavigationPointers()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create 3 siblings: fork-1 <-> fork-2 <-> fork-3
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        var fork2 = await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        var fork3 = await agent.ForkBranchAsync(main, "fork-3", fromMessageIndex: 0);

        // Act - Delete middle sibling (fork-2, which is at index 2 in: main(0), fork-1(1), fork-2(2), fork-3(3))
        await agent.DeleteBranchAsync("test-session", "fork-2");

        // Assert - Navigation pointers updated: main(0) <-> fork-1(1) <-> fork-3(2)
        var afterMain = await store.LoadBranchAsync("test-session", "main");
        var afterFork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var afterFork3 = await store.LoadBranchAsync("test-session", "fork-3");

        Assert.Null(afterMain!.PreviousSiblingId); // First in group
        Assert.Equal("fork-1", afterMain.NextSiblingId);

        Assert.Equal("main", afterFork1!.PreviousSiblingId);
        Assert.Equal("fork-3", afterFork1.NextSiblingId);

        Assert.Equal("fork-1", afterFork3!.PreviousSiblingId);
        Assert.Null(afterFork3.NextSiblingId); // Last
    }

    [Fact]
    public async Task Test19_DeleteBranch_UpdatesParentChildBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create fork
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Verify parent has child
        var beforeMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Contains("fork-1", beforeMain!.ChildBranches);
        Assert.Equal(1, beforeMain.TotalForks);

        // Act - Delete fork
        await agent.DeleteBranchAsync("test-session", "fork-1");

        // Assert - Parent updated
        var afterMain = await store.LoadBranchAsync("test-session", "main");
        Assert.DoesNotContain("fork-1", afterMain!.ChildBranches);
        Assert.Equal(0, afterMain.TotalForks);
    }

    [Fact]
    public async Task Test40_CannotDeleteMainBranch()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        await store.SaveBranchAsync("test-session", main);

        // Act & Assert - Cannot delete main branch
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await agent.DeleteBranchAsync("test-session", "main"));

        Assert.Contains("main", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test43_ForkWithInvalidMessageIndex_FailsGracefully()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act & Assert - Fork at invalid index
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 999));

        Assert.Contains("index", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    //──────────────────────────────────────────────────────────────────
    // INTEGRATION TESTS - WORKFLOWS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test33_CreateSession_ForkBranch_VerifyTreeStructure()
    {
        // Arrange & Act
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(AssistantMessage("Response 1"));
        main.AddMessage(UserMessage("Message 2"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 1);

        // Assert - Tree structure
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");

        // Main
        Assert.Equal(3, reloadedMain!.MessageCount);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Contains("fork-1", reloadedMain.ChildBranches);

        // Fork1
        Assert.Equal(2, reloadedFork1!.MessageCount); // 0, 1
        Assert.False(reloadedFork1.IsOriginal);
        Assert.Equal("main", reloadedFork1.ForkedFrom);
        Assert.Equal(1, reloadedFork1.ForkedAtMessageIndex);
        Assert.Empty(reloadedFork1.ChildBranches);
    }

    [Fact]
    public async Task Test34_ForkMultipleBranches_VerifySiblingOrder()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - Create 5 siblings
        var forks = new List<Branch>();
        for (int i = 0; i < 5; i++)
        {
            var fork = await agent.ForkBranchAsync(main, $"fork-{i}", fromMessageIndex: 0);
            forks.Add(fork);
            main = (await store.LoadBranchAsync("test-session", "main"))!;
            main.Session = session;
            await Task.Delay(10); // Ensure chronological order
        }

        // Assert - main is slot 0, fork-0 is slot 1, ..., fork-4 is slot 5. TotalSiblings=6.
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(6, reloadedMain.TotalSiblings);
        Assert.Null(reloadedMain.PreviousSiblingId);
        Assert.Equal("fork-0", reloadedMain.NextSiblingId);

        for (int i = 0; i < 5; i++)
        {
            var reloaded = await store.LoadBranchAsync("test-session", $"fork-{i}");
            Assert.Equal(i + 1, reloaded!.SiblingIndex); // +1 because main is slot 0
            Assert.Equal(6, reloaded.TotalSiblings);

            // Verify navigation pointers
            if (i == 0)
                Assert.Equal("main", reloaded.PreviousSiblingId);
            else
                Assert.Equal($"fork-{i - 1}", reloaded.PreviousSiblingId);

            if (i == 4)
                Assert.Null(reloaded.NextSiblingId);
            else
                Assert.Equal($"fork-{i + 1}", reloaded.NextSiblingId);
        }
    }

    [Fact]
    public async Task Test35_DeleteBranch_VerifySiblingsReindexed()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create 5 siblings
        for (int i = 0; i < 5; i++)
        {
            await agent.ForkBranchAsync(main, $"fork-{i}", fromMessageIndex: 0);
            main = (await store.LoadBranchAsync("test-session", "main"))!;
            main.Session = session;
        }

        // Act - Delete fork-2 (which is at index 3 in group: main(0), fork-0(1), fork-1(2), fork-2(3), fork-3(4), fork-4(5))
        await agent.DeleteBranchAsync("test-session", "fork-2");

        // Assert - Remaining: main(0), fork-0(1), fork-1(2), fork-3(3), fork-4(4) — TotalSiblings=5
        Assert.Null(await store.LoadBranchAsync("test-session", "fork-2"));

        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var fork0 = await store.LoadBranchAsync("test-session", "fork-0");
        var fork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var fork3 = await store.LoadBranchAsync("test-session", "fork-3");
        var fork4 = await store.LoadBranchAsync("test-session", "fork-4");

        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(1, fork0!.SiblingIndex);
        Assert.Equal(2, fork1!.SiblingIndex);
        Assert.Equal(3, fork3!.SiblingIndex); // Shifted from 4
        Assert.Equal(4, fork4!.SiblingIndex); // Shifted from 5

        // All have TotalSiblings=5
        Assert.Equal(5, reloadedMain.TotalSiblings);
        Assert.Equal(5, fork0.TotalSiblings);
        Assert.Equal(5, fork1.TotalSiblings);
        Assert.Equal(5, fork3.TotalSiblings);
        Assert.Equal(5, fork4.TotalSiblings);
    }

    [Fact]
    public async Task Test36_ForkAgain_VerifyMultiLevelAncestry()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - Create multi-level ancestry: main -> fork1 -> fork2
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        fork1.Session = session;
        fork1.AddMessage(AssistantMessage("Fork 1 response"));
        await store.SaveBranchAsync("test-session", fork1);

        var fork2 = await agent.ForkBranchAsync(fork1, "fork-2", fromMessageIndex: 0);

        // Assert - Verify ancestry chain
        Assert.Equal("main", fork1.ForkedFrom);
        Assert.Equal("fork-1", fork2.ForkedFrom);

        // Check Ancestors dictionary
        Assert.NotNull(fork2.Ancestors);
        Assert.Equal(2, fork2.Ancestors!.Count); // main, fork-1
        Assert.Equal("main", fork2.Ancestors["0"]);
        Assert.Equal("fork-1", fork2.Ancestors["1"]);

        // Check ChildBranches
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");

        Assert.Contains("fork-1", reloadedMain!.ChildBranches);
        Assert.Contains("fork-2", reloadedFork1!.ChildBranches);
    }

    //──────────────────────────────────────────────────────────────────
    // EDGE CASES
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestEdge_ForkEmptyBranch_AtIndexZero()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - Fork empty branch at index 0 (valid)
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Assert
        Assert.Empty(fork1.Messages);
        Assert.Equal("main", fork1.ForkedFrom);
        Assert.Equal(0, fork1.ForkedAtMessageIndex);
    }

    [Fact]
    public async Task TestEdge_ForkEmptyBranch_AtNonZeroIndex_Throws()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act & Assert - Fork empty branch at index 1 (invalid)
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 1));

        Assert.Contains("must be 0", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestEdge_DeleteLastSibling_LeavesNoSiblings()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Act - Delete the only fork
        await agent.DeleteBranchAsync("test-session", "fork-1");

        // Assert - Main is now alone with no siblings
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Empty(reloadedMain!.ChildBranches);
        Assert.Equal(0, reloadedMain.TotalForks);
    }

    //──────────────────────────────────────────────────────────────────
    // DELETE TESTS - SIBLING REDESIGN (source = slot 0)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task D1_DeleteOnlyFork_SourceReturnsToBareState()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create the one and only fork
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Verify fork exists and main is in a group of 2
        var beforeMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Equal(2, beforeMain!.TotalSiblings);
        Assert.Equal("fork-1", beforeMain.NextSiblingId);

        // Act - delete the only fork
        await agent.DeleteBranchAsync("test-session", "fork-1");

        // Assert - main returns to bare standalone state
        var afterMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Equal(1, afterMain!.TotalSiblings);
        Assert.Equal(0, afterMain.SiblingIndex);
        Assert.Null(afterMain.NextSiblingId);
        Assert.Null(afterMain.PreviousSiblingId);
    }

    [Fact]
    public async Task D2_DeleteFirstFork_SourcePointsToNewFirst()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create two forks: main(0), fork-1(1), fork-2(2)
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        await Task.Delay(10);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);

        // Act - delete fork-1 (which is at slot 1)
        await agent.DeleteBranchAsync("test-session", "fork-1");

        // Assert - remaining: main(0), fork-2(1). TotalSiblings=2
        var afterMain = await store.LoadBranchAsync("test-session", "main");
        var afterFork2 = await store.LoadBranchAsync("test-session", "fork-2");

        Assert.Equal(0, afterMain!.SiblingIndex);
        Assert.Equal(1, afterFork2!.SiblingIndex);
        Assert.Equal(2, afterMain.TotalSiblings);
        Assert.Equal(2, afterFork2.TotalSiblings);

        // main now points directly to fork-2
        Assert.Equal("fork-2", afterMain.NextSiblingId);
        Assert.Null(afterMain.PreviousSiblingId);
        Assert.Equal("main", afterFork2.PreviousSiblingId);
        Assert.Null(afterFork2.NextSiblingId);
    }

    [Fact]
    public async Task D3_DeleteSourceBranch_IsRejected()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Give main a fork so it's a real source branch with children
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Act & Assert - cannot delete main while it has forks
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await agent.DeleteBranchAsync("test-session", "main"));

        // Should mention either "main" or "child"
        Assert.True(
            ex.Message.Contains("main", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("child", StringComparison.OrdinalIgnoreCase),
            $"Expected error about 'main' or 'child', got: {ex.Message}");
    }

    [Fact]
    public async Task D4_DeleteAllForks_SourceIsAlone()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Create two forks
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        await Task.Delay(10);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);

        // Act - delete both forks sequentially
        await agent.DeleteBranchAsync("test-session", "fork-1");
        await agent.DeleteBranchAsync("test-session", "fork-2");

        // Assert - main is alone, all pointers cleared
        var finalMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Equal(1, finalMain!.TotalSiblings);
        Assert.Equal(0, finalMain.SiblingIndex);
        Assert.Null(finalMain.NextSiblingId);
        Assert.Null(finalMain.PreviousSiblingId);
        Assert.Empty(finalMain.ChildBranches);
    }

    //──────────────────────────────────────────────────────────────────
    // HELPER METHODS
    //──────────────────────────────────────────────────────────────────

    private async Task<HPD.Agent.Agent> CreateAgentWithStore(ISessionStore store)
    {
        return await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);
    }
}
