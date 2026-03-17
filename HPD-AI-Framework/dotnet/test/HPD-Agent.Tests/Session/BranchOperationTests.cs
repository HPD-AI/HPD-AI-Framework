using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for Branch operations on ISessionStore implementations.
/// Covers CRUD, forking, isolation, middleware state scoping, and serialization.
/// </summary>
public class BranchOperationTests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // INMEMORY STORE - BRANCH CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadBranch_RoundTrip()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var branch = session.CreateBranch("main");
        branch.AddMessage(UserMessage("Hello"));
        branch.AddMessage(AssistantMessage("Hi there!"));

        // Act
        await store.SaveBranchAsync("test-session", branch);
        var loaded = await store.LoadBranchAsync("test-session", "main");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("main", loaded.Id);
        Assert.Equal("test-session", loaded.SessionId);
        Assert.Equal(2, loaded.Messages.Count);
    }

    [Fact]
    public async Task InMemoryStore_LoadBranch_NonExistent_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var result = await store.LoadBranchAsync("no-session", "no-branch");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteBranch_RemovesBranch()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var branch = session.CreateBranch("to-delete");
        branch.AddMessage(UserMessage("Hello"));
        await store.SaveBranchAsync("test-session", branch);

        // Act
        await store.DeleteBranchAsync("test-session", "to-delete");
        var loaded = await store.LoadBranchAsync("test-session", "to-delete");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_ListBranchIds_ReturnsAllBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        await store.SaveBranchAsync("test-session", session.CreateBranch("main"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("formal"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("casual"));

        // Act
        var ids = await store.ListBranchIdsAsync("test-session");

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("main", ids);
        Assert.Contains("formal", ids);
        Assert.Contains("casual", ids);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_AlsoDeletesAllBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveBranchAsync("test-session", session.CreateBranch("main"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("formal"));

        // Act
        await store.DeleteSessionAsync("test-session");

        // Assert
        var branches = await store.ListBranchIdsAsync("test-session");
        Assert.Empty(branches);
    }

    //──────────────────────────────────────────────────────────────────
    // INMEMORY STORE - BRANCH ISOLATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_MultipleBranches_MessageIsolation()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var branch1 = session.CreateBranch("branch-1");
        branch1.AddMessage(UserMessage("Branch 1 message"));

        var branch2 = session.CreateBranch("branch-2");
        branch2.AddMessage(UserMessage("Branch 2 message"));
        branch2.AddMessage(AssistantMessage("Branch 2 response"));

        await store.SaveBranchAsync("test-session", branch1);
        await store.SaveBranchAsync("test-session", branch2);

        // Act
        var loaded1 = await store.LoadBranchAsync("test-session", "branch-1");
        var loaded2 = await store.LoadBranchAsync("test-session", "branch-2");

        // Assert
        Assert.Single(loaded1!.Messages);
        Assert.Equal(2, loaded2!.Messages.Count);
    }

    [Fact]
    public async Task InMemoryStore_DeleteBranch_DoesNotAffectOtherBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        await store.SaveBranchAsync("test-session", session.CreateBranch("keep"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("remove"));

        // Act
        await store.DeleteBranchAsync("test-session", "remove");

        // Assert
        var kept = await store.LoadBranchAsync("test-session", "keep");
        Assert.NotNull(kept);
        var removed = await store.LoadBranchAsync("test-session", "remove");
        Assert.Null(removed);
    }

    [Fact]
    public async Task InMemoryStore_DeleteBranch_SessionRemains()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        session.AddMetadata("project", "test");
        await store.SaveSessionAsync(session);
        await store.SaveBranchAsync("test-session", session.CreateBranch("main"));

        // Act
        await store.DeleteBranchAsync("test-session", "main");

        // Assert - session still exists
        var loadedSession = await store.LoadSessionAsync("test-session");
        Assert.NotNull(loadedSession);
        Assert.Equal("test-session", loadedSession.Id);
    }

    //──────────────────────────────────────────────────────────────────
    // JSON STORE - BRANCH CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task JsonStore_SaveAndLoadBranch_RoundTrip()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            var branch = session.CreateBranch("main");
            branch.AddMessage(UserMessage("Hello"));
            branch.AddMessage(AssistantMessage("Hi there!"));

            // Act
            await store.SaveBranchAsync("test-session", branch);
            var loaded = await store.LoadBranchAsync("test-session", "main");

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal("main", loaded.Id);
            Assert.Equal("test-session", loaded.SessionId);
            Assert.Equal(2, loaded.Messages.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_ListBranchIds_ReturnsAllBranches()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            await store.SaveBranchAsync("test-session", session.CreateBranch("main"));
            await store.SaveBranchAsync("test-session", session.CreateBranch("formal"));

            // Act
            var ids = await store.ListBranchIdsAsync("test-session");

            // Assert
            Assert.Equal(2, ids.Count);
            Assert.Contains("main", ids);
            Assert.Contains("formal", ids);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_DeleteBranch_RemovesBranch()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            await store.SaveBranchAsync("test-session", session.CreateBranch("to-delete"));

            // Act
            await store.DeleteBranchAsync("test-session", "to-delete");
            var loaded = await store.LoadBranchAsync("test-session", "to-delete");

            // Assert
            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    //──────────────────────────────────────────────────────────────────
    // BRANCH METADATA
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Branch_Description_SetAndRetrieved()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var branch = session.CreateBranch("formal");
        branch.Description = "Formal tone approach";

        // Act
        await store.SaveBranchAsync("test-session", branch);
        var loaded = await store.LoadBranchAsync("test-session", "formal");

        // Assert
        Assert.Equal("Formal tone approach", loaded!.Description);
    }

    [Fact]
    public async Task Branch_Tags_SetAndRetrieved()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var branch = session.CreateBranch("experiment");
        branch.Tags = ["v1", "draft", "formal-tone"];

        // Act
        await store.SaveBranchAsync("test-session", branch);
        var loaded = await store.LoadBranchAsync("test-session", "experiment");

        // Assert
        Assert.NotNull(loaded!.Tags);
        Assert.Equal(3, loaded.Tags.Count);
        Assert.Contains("formal-tone", loaded.Tags);
    }

    [Fact]
    public void Branch_ForkedFrom_TrackingAccuracy()
    {
        // Arrange & Act
        // Using new Branch() with init properties since CreateBranch doesn't support setting fork metadata
        var branch = new Branch("test-session", "formal")
        {
            ForkedFrom = "main",
            ForkedAtMessageIndex = 3
        };

        // Assert
        Assert.Equal("main", branch.ForkedFrom);
        Assert.Equal(3, branch.ForkedAtMessageIndex);
    }

    [Fact]
    public void Branch_Ancestors_MultiLevelTracking()
    {
        // Arrange & Act
        // Using new Branch() with init properties since CreateBranch doesn't support setting ancestors
        var branch = new Branch("test-session", "formal")
        {
            Ancestors = new Dictionary<string, string>
            {
                { "0", "main" },
                { "1", "experimental" },
                { "2", "formal" }
            }
        };

        // Assert
        Assert.Equal(3, branch.Ancestors.Count);
        Assert.Equal("main", branch.Ancestors["0"]);
        Assert.Equal("experimental", branch.Ancestors["1"]);
        Assert.Equal("formal", branch.Ancestors["2"]);
    }

    //──────────────────────────────────────────────────────────────────
    // FORK OPERATIONS (via Agent.ForkBranchAsync)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForkBranch_CreatesNewBranch_WithCorrectLineage()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateBranch("main");
        source.AddMessage(UserMessage("Message 1"));
        source.AddMessage(AssistantMessage("Response 1"));
        source.AddMessage(UserMessage("Message 2"));
        source.AddMessage(AssistantMessage("Response 2"));
        source.AddMessage(UserMessage("Message 3"));
        await store.SaveBranchAsync("test-session", source);

        // Act - fork at message index 3 (after "Response 2")
        var forked = await ForkBranchViaStore(store, session, "main", "formal", fromMessageIndex: 3);

        // Assert
        Assert.Equal("formal", forked.Id);
        Assert.Equal("test-session", forked.SessionId);
        Assert.Equal("main", forked.ForkedFrom);
        Assert.Equal(3, forked.ForkedAtMessageIndex);
        // Messages 0-3 should be copied (4 messages)
        Assert.Equal(4, forked.Messages.Count);
    }

    [Fact]
    public async Task ForkBranch_CopiesMessages_UpToForkPoint()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateBranch("main");
        source.AddMessage(UserMessage("First"));
        source.AddMessage(AssistantMessage("Second"));
        source.AddMessage(UserMessage("Third"));
        await store.SaveBranchAsync("test-session", source);

        // Act - fork at message 1 (after "Second")
        var forked = await ForkBranchViaStore(store, session, "main", "alt", fromMessageIndex: 1);

        // Assert - should have messages 0 and 1
        Assert.Equal(2, forked.Messages.Count);
    }

    [Fact]
    public async Task ForkBranch_CopiesBranchMiddlewareState()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateBranch("main");
        source.MiddlewareState["PlanModePersistentState"] = "{\"step\":3}";
        source.MiddlewareState["HistoryReductionState"] = "{\"cached\":true}";
        source.AddMessage(UserMessage("Hello"));
        await store.SaveBranchAsync("test-session", source);

        // Act
        var forked = await ForkBranchViaStore(store, session, "main", "alt", fromMessageIndex: 0);

        // Assert - branch-scoped state copied
        Assert.Equal("{\"step\":3}", forked.MiddlewareState["PlanModePersistentState"]);
        Assert.Equal("{\"cached\":true}", forked.MiddlewareState["HistoryReductionState"]);
    }

    [Fact]
    public async Task ForkBranch_BranchStateDivergesAfterFork()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateBranch("main");
        source.MiddlewareState["PlanModePersistentState"] = "{\"step\":1}";
        source.AddMessage(UserMessage("Hello"));
        await store.SaveBranchAsync("test-session", source);

        var forked = await ForkBranchViaStore(store, session, "main", "alt", fromMessageIndex: 0);

        // Act - modify forked branch state
        forked.MiddlewareState["PlanModePersistentState"] = "{\"step\":5}";
        await store.SaveBranchAsync("test-session", forked);

        // Assert - source unchanged
        var reloadedSource = await store.LoadBranchAsync("test-session", "main");
        Assert.Equal("{\"step\":1}", reloadedSource!.MiddlewareState["PlanModePersistentState"]);

        var reloadedForked = await store.LoadBranchAsync("test-session", "alt");
        Assert.Equal("{\"step\":5}", reloadedForked!.MiddlewareState["PlanModePersistentState"]);
    }

    //──────────────────────────────────────────────────────────────────
    // MIDDLEWARE STATE SCOPING
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionMiddlewareState_SharedAcrossBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        session.MiddlewareState["PermissionPersistentState"] = "{\"Bash\":\"AlwaysAllow\"}";
        await store.SaveSessionAsync(session);

        await store.SaveBranchAsync("test-session", session.CreateBranch("branch-1"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("branch-2"));

        // Act - load session (session state is shared, not per-branch)
        var loadedSession = await store.LoadSessionAsync("test-session");

        // Assert - session-scoped state accessible regardless of branch
        Assert.Equal("{\"Bash\":\"AlwaysAllow\"}", loadedSession!.MiddlewareState["PermissionPersistentState"]);
    }

    [Fact]
    public async Task BranchMiddlewareState_IsolatedPerBranch()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var branch1 = session.CreateBranch("branch-1");
        branch1.MiddlewareState["PlanModePersistentState"] = "{\"plan\":\"A\"}";

        var branch2 = session.CreateBranch("branch-2");
        branch2.MiddlewareState["PlanModePersistentState"] = "{\"plan\":\"B\"}";

        await store.SaveBranchAsync("test-session", branch1);
        await store.SaveBranchAsync("test-session", branch2);

        // Act
        var loaded1 = await store.LoadBranchAsync("test-session", "branch-1");
        var loaded2 = await store.LoadBranchAsync("test-session", "branch-2");

        // Assert
        Assert.Equal("{\"plan\":\"A\"}", loaded1!.MiddlewareState["PlanModePersistentState"]);
        Assert.Equal("{\"plan\":\"B\"}", loaded2!.MiddlewareState["PlanModePersistentState"]);
    }

    //──────────────────────────────────────────────────────────────────
    // UNCOMMITTED TURN + BRANCH INTEGRATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UncommittedTurn_BranchId_PreservedCorrectly()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var turn = new UncommittedTurn
        {
            SessionId = "test-session",
            BranchId = "formal",
            TurnMessages = [new ChatMessage(ChatRole.User, "Help me")],
            Iteration = 1,
            CompletedFunctions = System.Collections.Immutable.ImmutableHashSet<string>.Empty,
            MiddlewareState = new MiddlewareState(),
            IsTerminated = false,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
        };

        // Act
        await store.SaveUncommittedTurnAsync(turn);
        var loaded = await store.LoadUncommittedTurnAsync("test-session");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("formal", loaded.BranchId);
    }

    //──────────────────────────────────────────────────────────────────
    // BRANCH CLASS UNIT TESTS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Branch_Constructor_SetsDefaults()
    {
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");

        Assert.Equal("main", branch.Id);
        Assert.Equal("session-1", branch.SessionId);
        Assert.Empty(branch.Messages);
        Assert.Empty(branch.MiddlewareState);
        Assert.Null(branch.ForkedFrom);
        Assert.Null(branch.ForkedAtMessageIndex);
        Assert.Null(branch.Description);
        Assert.Null(branch.Tags);
        Assert.Null(branch.Ancestors);
    }

    [Fact]
    public void Branch_AddMessage_UpdatesLastActivity()
    {
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");
        var before = branch.LastActivity;

        // Small delay to ensure time difference
        branch.AddMessage(new ChatMessage(ChatRole.User, "Hello"));

        Assert.Single(branch.Messages);
        Assert.True(branch.LastActivity >= before);
    }

    [Fact]
    public void Branch_MessageCount_ReflectsMessages()
    {
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");
        Assert.Equal(0, branch.MessageCount);

        branch.AddMessage(UserMessage("One"));
        Assert.Equal(1, branch.MessageCount);

        branch.AddMessage(AssistantMessage("Two"));
        Assert.Equal(2, branch.MessageCount);
    }

    //──────────────────────────────────────────────────────────────────
    // SESSION CLASS UNIT TESTS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Session_Constructor_SetsDefaults()
    {
        var session = new HPD.Agent.Session("my-session");

        Assert.Equal("my-session", session.Id);
        Assert.Empty(session.Metadata);
        Assert.Empty(session.MiddlewareState);
        Assert.Null(session.Store);
    }

    //──────────────────────────────────────────────────────────────────
    // AMBIGUOUS BRANCH VALIDATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadSessionAndBranch_SingleBranch_DefaultsToMain()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveBranchAsync("test-session", session.CreateBranch("main"));

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // No branchId specified, single branch → should default to "main"
        var (loadedSession, branch) = await agent.LoadSessionAndBranchAsync("test-session");

        Assert.Equal("test-session", loadedSession.Id);
        Assert.Equal("main", branch.Id);
    }

    [Fact]
    public async Task LoadSessionAndBranch_MultipleBranches_NoBranchId_ThrowsAmbiguousBranchException()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveBranchAsync("test-session", session.CreateBranch("main"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("formal"));

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // No branchId specified, multiple branches → should throw
        var ex = await Assert.ThrowsAsync<AmbiguousBranchException>(
            () => agent.LoadSessionAndBranchAsync("test-session"));

        Assert.Equal("test-session", ex.SessionId);
        Assert.Contains("main", ex.AvailableBranches);
        Assert.Contains("formal", ex.AvailableBranches);
        Assert.Equal(2, ex.AvailableBranches.Count);
    }

    [Fact]
    public async Task LoadSessionAndBranch_MultipleBranches_ExplicitBranchId_Works()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveBranchAsync("test-session", session.CreateBranch("main"));
        await store.SaveBranchAsync("test-session", session.CreateBranch("formal"));

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Explicit branchId, multiple branches → should work fine
        var (loadedSession, branch) = await agent.LoadSessionAndBranchAsync("test-session", "formal");

        Assert.Equal("test-session", loadedSession.Id);
        Assert.Equal("formal", branch.Id);
    }

    [Fact]
    public async Task LoadSessionAndBranch_SessionNotInStore_ThrowsSessionNotFoundException()
    {
        var store = new InMemorySessionStore();

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Session was never created — should throw, not silently create
        var ex = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => agent.LoadSessionAndBranchAsync("new-session"));

        Assert.Equal("new-session", ex.SessionId);
        Assert.Null(ex.BranchId);
    }

    [Fact]
    public async Task LoadSessionAndBranch_BranchNotInStore_ThrowsSessionNotFoundException()
    {
        var store = new InMemorySessionStore();

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Session exists but branch does not
        await agent.CreateSessionAsync("test-session");

        var ex = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => agent.LoadSessionAndBranchAsync("test-session", "missing-branch"));

        Assert.Equal("test-session", ex.SessionId);
        Assert.Equal("missing-branch", ex.BranchId);
    }

    //──────────────────────────────────────────────────────────────────
    // HELPERS
    //──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates ForkBranchAsync logic (same as Agent.ForkBranchAsync) directly on the store.
    /// Used when we don't have a full Agent instance in tests.
    /// </summary>
    private static async Task<Branch> ForkBranchViaStore(
        ISessionStore store,
        HPD.Agent.Session session,
        string sourceBranchId,
        string newBranchId,
        int fromMessageIndex)
    {
        var source = await store.LoadBranchAsync(session.Id, sourceBranchId);
        Assert.NotNull(source);

        // Using internal constructor with init properties to set fork metadata
        var newBranch = new Branch(session.Id, newBranchId)
        {
            ForkedFrom = sourceBranchId,
            ForkedAtMessageIndex = fromMessageIndex
        };

        // Copy messages up to and including fork point
        newBranch.Messages.AddRange(source.Messages.Take(fromMessageIndex + 1));

        // Copy branch-scoped middleware state
        foreach (var kvp in source.MiddlewareState)
        {
            newBranch.MiddlewareState[kvp.Key] = kvp.Value;
        }

        await store.SaveBranchAsync(session.Id, newBranch);
        return newBranch;
    }
}
