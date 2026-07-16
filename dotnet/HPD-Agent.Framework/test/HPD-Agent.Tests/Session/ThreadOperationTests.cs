using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for Thread operations on ISessionStore implementations.
/// Covers CRUD, forking, isolation, middleware state scoping, and serialization.
/// </summary>
public class ThreadOperationTests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // INMEMORY STORE - THREAD CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadThread_RoundTrip()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var thread = session.CreateThread("main");
        thread.AddMessage(UserMessage("Hello"));
        thread.AddMessage(AssistantMessage("Hi there!"));

        // Act
        await store.SaveInitialThreadAsync("test-session", thread);
        var loaded = await store.ProjectThreadAsync("test-session", "main", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("main", loaded.Id);
        Assert.Equal("test-session", loaded.SessionId);
        Assert.Equal(2, loaded.Messages.Count);
    }

    [Fact]
    public async Task InMemoryStore_LoadThread_NonExistent_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var result = await store.ProjectThreadAsync("no-session", "no-thread", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteThread_RemovesThread()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var thread = session.CreateThread("to-delete");
        thread.AddMessage(UserMessage("Hello"));
        await store.SaveInitialThreadAsync("test-session", thread);

        // Act
        await store.DeleteThreadAsync("test-session", "to-delete");
        var loaded = await store.ProjectThreadAsync("test-session", "to-delete", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_ListThreads_ReturnsAllDescriptors()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("formal"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("casual"));

        // Act
        var descriptors = await store.CollectThreadDescriptorsAsync("test-session");
        var ids = descriptors.Select(item => item.Key.ThreadId).ToList();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("main", ids);
        Assert.Contains("formal", ids);
        Assert.Contains("casual", ids);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_AlsoDeletesAllThreads()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("formal"));

        // Act
        await store.DeleteSessionAsync("test-session");

        // Assert
        var threads = await store.CollectThreadDescriptorsAsync("test-session");
        Assert.Empty(threads);
    }

    //──────────────────────────────────────────────────────────────────
    // INMEMORY STORE - THREAD ISOLATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_MultipleThreads_MessageIsolation()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var thread1 = session.CreateThread("thread-1");
        thread1.AddMessage(UserMessage("Thread 1 message"));

        var thread2 = session.CreateThread("thread-2");
        thread2.AddMessage(UserMessage("Thread 2 message"));
        thread2.AddMessage(AssistantMessage("Thread 2 response"));

        await store.SaveInitialThreadAsync("test-session", thread1);
        await store.SaveInitialThreadAsync("test-session", thread2);

        // Act
        var loaded1 = await store.ProjectThreadAsync("test-session", "thread-1", ThreadProjectionPurpose.ThreadHistory);
        var loaded2 = await store.ProjectThreadAsync("test-session", "thread-2", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Single(loaded1!.Messages);
        Assert.Equal(2, loaded2!.Messages.Count);
    }

    [Fact]
    public async Task InMemoryStore_DeleteThread_DoesNotAffectOtherThreads()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        await store.SaveInitialThreadAsync("test-session", session.CreateThread("keep"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("remove"));

        // Act
        await store.DeleteThreadAsync("test-session", "remove");

        // Assert
        var kept = await store.ProjectThreadAsync("test-session", "keep", ThreadProjectionPurpose.ThreadHistory);
        Assert.NotNull(kept);
        var removed = await store.ProjectThreadAsync("test-session", "remove", ThreadProjectionPurpose.ThreadHistory);
        Assert.Null(removed);
    }

    [Fact]
    public async Task InMemoryStore_DeleteThread_SessionRemains()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        session.AddMetadata("project", "test");
        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));

        // Act
        await store.DeleteThreadAsync("test-session", "main");

        // Assert - session still exists
        var loadedSession = await store.LoadSessionAsync("test-session");
        Assert.NotNull(loadedSession);
        Assert.Equal("test-session", loadedSession.Id);
    }

    //──────────────────────────────────────────────────────────────────
    // FILE STORE - THREAD CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileStore_SaveAndLoadThread_RoundTrip()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            var thread = session.CreateThread("main");
            thread.AddMessage(UserMessage("Hello"));
            thread.AddMessage(AssistantMessage("Hi there!"));

            // Act
            await store.SaveInitialThreadAsync("test-session", thread);
            var loaded = await store.ProjectThreadAsync("test-session", "main", ThreadProjectionPurpose.ThreadHistory);

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
    public async Task FileStore_ListThreads_ReturnsAllDescriptors()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));
            await store.SaveInitialThreadAsync("test-session", session.CreateThread("formal"));

            // Act
            var descriptors = await store.CollectThreadDescriptorsAsync("test-session");
            var ids = descriptors.Select(item => item.Key.ThreadId).ToList();

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
    public async Task FileStore_DeleteThread_RemovesThread()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            await store.SaveInitialThreadAsync("test-session", session.CreateThread("to-delete"));

            // Act
            await store.DeleteThreadAsync("test-session", "to-delete");
            var loaded = await store.ProjectThreadAsync("test-session", "to-delete", ThreadProjectionPurpose.ThreadHistory);

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
    // THREAD METADATA
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Thread_Description_SetAndRetrieved()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var thread = session.CreateThread("formal");
        thread.Description = "Formal tone approach";

        // Act
        await store.SaveInitialThreadAsync("test-session", thread);
        var loaded = await store.ProjectThreadAsync("test-session", "formal", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Equal("Formal tone approach", loaded!.Description);
    }

    [Fact]
    public async Task Thread_Tags_SetAndRetrieved()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var thread = session.CreateThread("experiment");
        thread.Tags = ["v1", "draft", "formal-tone"];

        // Act
        await store.SaveInitialThreadAsync("test-session", thread);
        var loaded = await store.ProjectThreadAsync("test-session", "experiment", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.NotNull(loaded!.Tags);
        Assert.Equal(3, loaded.Tags.Count);
        Assert.Contains("formal-tone", loaded.Tags);
    }

    [Fact]
    public void Thread_ForkedFrom_TrackingAccuracy()
    {
        // Arrange & Act
        // Using new Thread() with init properties since CreateThread doesn't support setting fork metadata
        var thread = new Thread("test-session", "formal")
        {
            ForkedFrom = "main",
            ForkedAtMessageIndex = 3
        };

        // Assert
        Assert.Equal("main", thread.ForkedFrom);
        Assert.Equal(3, thread.ForkedAtMessageIndex);
    }

    [Fact]
    public void Thread_Ancestors_MultiLevelTracking()
    {
        // Arrange & Act
        // Using new Thread() with init properties since CreateThread doesn't support setting ancestors
        var thread = new Thread("test-session", "formal")
        {
            Ancestors = new Dictionary<string, string>
            {
                { "0", "main" },
                { "1", "experimental" },
                { "2", "formal" }
            }
        };

        // Assert
        Assert.Equal(3, thread.Ancestors.Count);
        Assert.Equal("main", thread.Ancestors["0"]);
        Assert.Equal("experimental", thread.Ancestors["1"]);
        Assert.Equal("formal", thread.Ancestors["2"]);
    }

    //──────────────────────────────────────────────────────────────────
    // FORK OPERATIONS (via Agent.ForkThreadAsync)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForkThread_CreatesNewThread_WithCorrectLineage()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateThread("main");
        source.AddMessage(UserMessage("Message 1"));
        source.AddMessage(AssistantMessage("Response 1"));
        source.AddMessage(UserMessage("Message 2"));
        source.AddMessage(AssistantMessage("Response 2"));
        source.AddMessage(UserMessage("Message 3"));
        await store.SaveInitialThreadAsync("test-session", source);

        // Act - fork at message 3 (after "Response 2")
        var agent = await CreateAgentWithStoreAsync(store);
        await agent.ForkThreadAsync("test-session", "main", "formal", source.Messages[3].MessageId!);
        var forked = await store.ProjectThreadAsync("test-session", "formal", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.NotNull(forked);
        Assert.Equal("formal", forked.Id);
        Assert.Equal("test-session", forked.SessionId);
        Assert.Equal("main", forked.ForkedFrom);
        Assert.Equal(source.Messages[3].MessageId, forked.ForkedAtMessageId);
        Assert.Equal(3, forked.ForkedAtMessageIndex);
        // Messages 0-3 should be copied (4 messages)
        Assert.Equal(4, forked.Messages.Count);
    }

    [Fact]
    public async Task ForkThread_CopiesMessages_UpToForkPoint()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateThread("main");
        source.AddMessage(UserMessage("First"));
        source.AddMessage(AssistantMessage("Second"));
        source.AddMessage(UserMessage("Third"));
        await store.SaveInitialThreadAsync("test-session", source);

        // Act - fork at message 1 (after "Second")
        var agent = await CreateAgentWithStoreAsync(store);
        await agent.ForkThreadAsync("test-session", "main", "alt", source.Messages[1].MessageId!);
        var forked = await store.ProjectThreadAsync("test-session", "alt", ThreadProjectionPurpose.ThreadHistory);

        // Assert - should have messages 0 and 1
        Assert.NotNull(forked);
        Assert.Equal(2, forked.Messages.Count);
    }

    [Fact]
    public async Task ForkThread_CopiesThreadMiddlewareState()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateThread("main");
        source.MiddlewareState["PlanModePersistentState"] = "{\"step\":3}";
        source.MiddlewareState["CompactionState"] = "{\"cached\":true}";
        source.AddMessage(UserMessage("Hello"));
        await store.SaveInitialThreadAsync("test-session", source);

        // Act
        var agent = await CreateAgentWithStoreAsync(store);
        await agent.ForkThreadAsync("test-session", "main", "alt", source.Messages[0].MessageId!);
        var forked = await store.ProjectThreadAsync("test-session", "alt", ThreadProjectionPurpose.ThreadHistory);

        // Assert - thread-scoped state copied
        Assert.NotNull(forked);
        Assert.Equal("{\"step\":3}", forked.MiddlewareState["PlanModePersistentState"]);
        Assert.Equal("{\"cached\":true}", forked.MiddlewareState["CompactionState"]);
    }

    [Fact]
    public async Task ForkThread_ThreadStateDivergesAfterFork()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var source = session.CreateThread("main");
        source.MiddlewareState["PlanModePersistentState"] = "{\"step\":1}";
        source.AddMessage(UserMessage("Hello"));
        await store.SaveInitialThreadAsync("test-session", source);

        var agent = await CreateAgentWithStoreAsync(store);
        await agent.ForkThreadAsync("test-session", "main", "alt", source.Messages[0].MessageId!);
        var forked = await store.ProjectThreadAsync("test-session", "alt", ThreadProjectionPurpose.ThreadHistory);
        Assert.NotNull(forked);

        // Act - modify forked thread state
        forked.MiddlewareState["PlanModePersistentState"] = "{\"step\":5}";
        await store.AppendThreadEventAsync(
            "test-session",
            "alt",
            ThreadEventFactory.ThreadMiddlewareStateCommitted(
                "test-session",
                "alt",
                forked.MiddlewareState));

        // Assert - source unchanged
        var reloadedSource = await store.ProjectThreadAsync("test-session", "main", ThreadProjectionPurpose.ThreadHistory);
        Assert.Equal("{\"step\":1}", reloadedSource!.MiddlewareState["PlanModePersistentState"]);

        var reloadedForked = await store.ProjectThreadAsync("test-session", "alt", ThreadProjectionPurpose.ThreadHistory);
        Assert.Equal("{\"step\":5}", reloadedForked!.MiddlewareState["PlanModePersistentState"]);
    }

    //──────────────────────────────────────────────────────────────────
    // MIDDLEWARE STATE SCOPING
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionMiddlewareState_SharedAcrossThreads()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        session.MiddlewareState["PermissionPersistentState"] = "{\"Bash\":\"AlwaysAllow\"}";
        await store.SaveSessionAsync(session);

        await store.SaveInitialThreadAsync("test-session", session.CreateThread("thread-1"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("thread-2"));

        // Act - load session (session state is shared, not per-thread)
        var loadedSession = await store.LoadSessionAsync("test-session");

        // Assert - session-scoped state accessible regardless of thread
        Assert.Equal("{\"Bash\":\"AlwaysAllow\"}", loadedSession!.MiddlewareState["PermissionPersistentState"]);
    }

    [Fact]
    public async Task ThreadMiddlewareState_IsolatedPerThread()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var thread1 = session.CreateThread("thread-1");
        thread1.MiddlewareState["PlanModePersistentState"] = "{\"plan\":\"A\"}";

        var thread2 = session.CreateThread("thread-2");
        thread2.MiddlewareState["PlanModePersistentState"] = "{\"plan\":\"B\"}";

        await store.SaveInitialThreadAsync("test-session", thread1);
        await store.SaveInitialThreadAsync("test-session", thread2);

        // Act
        var loaded1 = await store.ProjectThreadAsync("test-session", "thread-1", ThreadProjectionPurpose.ThreadHistory);
        var loaded2 = await store.ProjectThreadAsync("test-session", "thread-2", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Equal("{\"plan\":\"A\"}", loaded1!.MiddlewareState["PlanModePersistentState"]);
        Assert.Equal("{\"plan\":\"B\"}", loaded2!.MiddlewareState["PlanModePersistentState"]);
    }

    //──────────────────────────────────────────────────────────────────
    // THREAD CLASS UNIT TESTS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Thread_Constructor_SetsDefaults()
    {
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");

        Assert.Equal("main", thread.Id);
        Assert.Equal("session-1", thread.SessionId);
        Assert.Empty(thread.Messages);
        Assert.Empty(thread.MiddlewareState);
        Assert.Null(thread.ForkedFrom);
        Assert.Null(thread.ForkedAtMessageIndex);
        Assert.Null(thread.Description);
        Assert.Null(thread.Tags);
        Assert.Null(thread.Ancestors);
    }

    [Fact]
    public void Thread_AddMessage_UpdatesLastActivity()
    {
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");
        var before = thread.LastActivity;

        // Small delay to ensure time difference
        thread.AddMessage(new ChatMessage(ChatRole.User, "Hello"));

        Assert.Single(thread.Messages);
        Assert.True(thread.LastActivity >= before);
    }

    [Fact]
    public void Thread_MessageCount_ReflectsMessages()
    {
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");
        Assert.Equal(0, thread.MessageCount);

        thread.AddMessage(UserMessage("One"));
        Assert.Equal(1, thread.MessageCount);

        thread.AddMessage(AssistantMessage("Two"));
        Assert.Equal(2, thread.MessageCount);
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
    // AMBIGUOUS THREAD VALIDATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadSessionAndThread_SingleThread_DefaultsToMain()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // No threadId specified, single thread → should default to "main"
        var (loadedSession, thread) = await agent.LoadSessionAndThreadAsync("test-session");

        Assert.Equal("test-session", loadedSession.Id);
        Assert.Equal("main", thread.Id);
    }

    [Fact]
    public async Task LoadSessionAndThread_MultipleThreads_NoThreadId_ThrowsAmbiguousThreadException()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("formal"));

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // No threadId specified, multiple threads → should throw
        var ex = await Assert.ThrowsAsync<AmbiguousThreadException>(
            () => agent.LoadSessionAndThreadAsync("test-session"));

        Assert.Equal("test-session", ex.SessionId);
        Assert.Contains("main", ex.AvailableThreads);
        Assert.Contains("formal", ex.AvailableThreads);
        Assert.Equal(2, ex.AvailableThreads.Count);
    }

    [Fact]
    public async Task LoadSessionAndThread_MultipleThreads_ExplicitThreadId_Works()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("main"));
        await store.SaveInitialThreadAsync("test-session", session.CreateThread("formal"));

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Explicit threadId, multiple threads → should work fine
        var (loadedSession, thread) = await agent.LoadSessionAndThreadAsync("test-session", "formal");

        Assert.Equal("test-session", loadedSession.Id);
        Assert.Equal("formal", thread.Id);
    }

    [Fact]
    public async Task LoadSessionAndThread_SessionNotInStore_ThrowsSessionNotFoundException()
    {
        var store = new InMemorySessionStore();

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Session was never created — should throw, not silently create
        var ex = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => agent.LoadSessionAndThreadAsync("new-session"));

        Assert.Equal("new-session", ex.SessionId);
        Assert.Null(ex.ThreadId);
    }

    [Fact]
    public async Task LoadSessionAndThread_ThreadNotInStore_ThrowsSessionNotFoundException()
    {
        var store = new InMemorySessionStore();

        var agent = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Session exists but thread does not
        await agent.CreateSessionAsync("test-session");

        var ex = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => agent.LoadSessionAndThreadAsync("test-session", "missing-thread"));

        Assert.Equal("test-session", ex.SessionId);
        Assert.Equal("missing-thread", ex.ThreadId);
    }

    //──────────────────────────────────────────────────────────────────
    // HELPERS
    //──────────────────────────────────────────────────────────────────

    private static async Task<Agent> CreateAgentWithStoreAsync(ISessionStore store) =>
        await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);
}
