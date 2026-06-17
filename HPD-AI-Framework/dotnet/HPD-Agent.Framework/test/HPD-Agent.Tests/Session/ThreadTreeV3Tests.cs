using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// V3 Thread Tree Architecture Tests
/// Tests for tree navigation, sibling ordering, atomic operations, and referential integrity.
/// </summary>
public class ThreadTreeV3Tests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // UNIT TESTS - SCHEMA & SERIALIZATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Test01_NewFields_SerializeDeserialize_RoundTrip()
    {
        // Arrange - Create thread with all V3 fields
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = 2,
            TotalSiblings = 5,
            IsOriginal = false,
            OriginalThreadId = "main",
            PreviousSiblingId = "thread-0",
            NextSiblingId = "thread-2",
            ChildThreads = new List<string> { "child-1", "child-2" }
        };

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(thread);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Thread>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.SiblingIndex);
        Assert.Equal(5, deserialized.TotalSiblings);
        Assert.False(deserialized.IsOriginal);
        Assert.Equal("main", deserialized.OriginalThreadId);
        Assert.Equal("thread-0", deserialized.PreviousSiblingId);
        Assert.Equal("thread-2", deserialized.NextSiblingId);
        Assert.Equal(2, deserialized.ChildThreads.Count);
        Assert.Contains("child-1", deserialized.ChildThreads);
    }

    [Fact]
    public void Test02_DefaultValues_PassInvariantChecks()
    {
        // Arrange & Act - Create new thread with defaults
        var thread = new Thread("session-1", "main");

        // Assert - Should not throw
        thread.ValidateTreeInvariants();

        // Verify defaults
        Assert.Equal(0, thread.SiblingIndex);
        Assert.Equal(1, thread.TotalSiblings);
        Assert.True(thread.IsOriginal);
        Assert.Null(thread.OriginalThreadId);
        Assert.Null(thread.PreviousSiblingId);
        Assert.Null(thread.NextSiblingId);
        Assert.Empty(thread.ChildThreads);
    }

    [Fact]
    public void Test03_ThreadCreation_SetsCorrectInitialValues()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");

        // Act
        var thread = session.CreateThread("main");

        // Assert - V3 defaults
        Assert.Equal(0, thread.SiblingIndex);
        Assert.Equal(1, thread.TotalSiblings);
        Assert.True(thread.IsOriginal);
        Assert.Null(thread.OriginalThreadId);
        Assert.Null(thread.PreviousSiblingId);
        Assert.Null(thread.NextSiblingId);
        Assert.Empty(thread.ChildThreads);
        Assert.Equal(0, thread.TotalForks);
    }

    [Fact]
    public void Test04_MissingTreeFields_Throws()
    {
        // Arrange - JSON without tree fields
        var jsonWithoutV3Fields = """
        {
            "Id": "thread-1",
            "SessionId": "session-1",
            "Messages": [],
            "ForkedFrom": null,
            "ForkedAtMessageIndex": null,
            "CreatedAt": "2025-01-01T00:00:00Z",
            "LastActivity": "2025-01-01T00:00:00Z",
            "Name": null,
            "Description": null,
            "Tags": null,
            "Ancestors": null,
            "MiddlewareState": {}
        }
        """;

        // Act
        var act = () => System.Text.Json.JsonSerializer.Deserialize<Thread>(jsonWithoutV3Fields);

        // Assert
        act.Should().Throw<System.Text.Json.JsonException>()
            .WithMessage("*totalSiblings*");
    }

    [Fact]
    public void Test05_NameField_ProperlySerializedDeserialized()
    {
        // Arrange
        var thread = new Thread("session-1", "thread-1")
        {
            Name = "Experiment A"
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(thread);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Thread>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("Experiment A", deserialized.Name);
    }

    //──────────────────────────────────────────────────────────────────
    // UNIT TESTS - VALIDATION (ValidateTreeInvariants)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Test26_IsOriginal_MismatchWithForkedFrom_ThrowsException()
    {
        // Arrange - Invalid state: IsOriginal=true but has ForkedFrom
        var thread = new Thread("session-1", "thread-1")
        {
            IsOriginal = true,
            ForkedFrom = "main", //  Conflict!
            SiblingIndex = 0,
            TotalSiblings = 1
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("IsOriginal=True but ForkedFrom=main", ex.Message);
    }

    [Fact]
    public void Test27_SiblingIndex_OutOfRange_ThrowsException()
    {
        // Arrange - SiblingIndex >= TotalSiblings
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = 5,
            TotalSiblings = 3, //  Index out of range!
            IsOriginal = false,
            ForkedFrom = "main"
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("out of range [0, 3)", ex.Message);
    }

    [Fact]
    public void Test28_TotalSiblings_ZeroOrNegative_ThrowsException()
    {
        // Arrange - Set SiblingIndex < TotalSiblings to bypass range check first
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = -1, // Set this negative to avoid range check triggering first
            TotalSiblings = 0, //  Must be positive!
            IsOriginal = true
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        // The validation will catch TotalSiblings <= 0 or SiblingIndex out of range
        Assert.True(ex.Message.Contains("must be positive") || ex.Message.Contains("out of range"));
    }

    [Fact]
    public void Test29_FirstSibling_WithPreviousPointer_ThrowsException()
    {
        // Arrange - First sibling should have no previous
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = 0,
            TotalSiblings = 3,
            IsOriginal = true,
            PreviousSiblingId = "some-id" //  First sibling can't have previous!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("First sibling (index=0) has PreviousSiblingId", ex.Message);
    }

    [Fact]
    public void Test30_LastSibling_WithNextPointer_ThrowsException()
    {
        // Arrange - Last sibling should have no next
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = 2,
            TotalSiblings = 3,
            IsOriginal = false,
            ForkedFrom = "main",
            NextSiblingId = "some-id" //  Last sibling can't have next!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("Last sibling (index=2) has NextSiblingId", ex.Message);
    }

    [Fact]
    public void Test31_MiddleSibling_MissingPreviousPointer_ThrowsException()
    {
        // Arrange - Middle sibling must have both pointers
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = 1,
            TotalSiblings = 3,
            IsOriginal = false,
            ForkedFrom = "main",
            PreviousSiblingId = null, //  Middle sibling needs previous!
            NextSiblingId = "thread-2"
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("Middle sibling (index=1) has null PreviousSiblingId", ex.Message);
    }

    [Fact]
    public void Test31b_MiddleSibling_MissingNextPointer_ThrowsException()
    {
        // Arrange - Middle sibling must have both pointers
        var thread = new Thread("session-1", "thread-1")
        {
            SiblingIndex = 1,
            TotalSiblings = 3,
            IsOriginal = false,
            ForkedFrom = "main",
            PreviousSiblingId = "main",
            NextSiblingId = null //  Middle sibling needs next!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("Middle sibling (index=1) has null NextSiblingId", ex.Message);
    }

    [Fact]
    public void Test32_OriginalThreadId_SetOnOriginalThread_ThrowsException()
    {
        // Arrange - Original thread shouldn't have OriginalThreadId
        var thread = new Thread("session-1", "main")
        {
            IsOriginal = true,
            ForkedFrom = null,
            OriginalThreadId = "some-id", //  Original threads don't have this!
            SiblingIndex = 0,
            TotalSiblings = 1
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => thread.ValidateTreeInvariants());
        Assert.Contains("Original thread should have OriginalThreadId=null", ex.Message);
    }

    //──────────────────────────────────────────────────────────────────
    // UNIT TESTS - HELPER PROPERTIES
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void TestHelpers_IsLeaf_ReturnsTrue_WhenNoChildren()
    {
        // Arrange
        var thread = new Thread("session-1", "thread-1");

        // Act & Assert
        Assert.True(thread.IsLeaf);
        Assert.Equal(0, thread.TotalForks);
    }

    [Fact]
    public void TestHelpers_IsLeaf_ReturnsFalse_WhenHasChildren()
    {
        // Arrange
        var thread = new Thread("session-1", "thread-1")
        {
            ChildThreads = new List<string> { "child-1", "child-2" }
        };

        // Act & Assert
        Assert.False(thread.IsLeaf);
        Assert.Equal(2, thread.TotalForks);
    }

    [Fact]
    public void TestHelpers_IsRoot_ReturnsTrue_WhenNoParent()
    {
        // Arrange
        var thread = new Thread("session-1", "main")
        {
            ForkedFrom = null
        };

        // Act & Assert
        Assert.True(thread.IsRoot);
    }

    [Fact]
    public void TestHelpers_IsRoot_ReturnsFalse_WhenHasParent()
    {
        // Arrange
        var thread = new Thread("session-1", "fork-1")
        {
            ForkedFrom = "main"
        };

        // Act & Assert
        Assert.False(thread.IsRoot);
    }

    //──────────────────────────────────────────────────────────────────
    // INTEGRATION TESTS - ATOMIC FORK
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test09_ForkThread_UpdatesAllSiblingMetadata_Atomically()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(AssistantMessage("Response 1"));
        main.AddMessage(UserMessage("Message 2"));
        await store.SaveInitialThreadAsync("test-session", main);

        // Act - Fork main thread
        main.Session = session;
        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[1].MessageId!);

        // Assert - Fork has correct metadata
        // main is sibling #0 (the original), fork-1 is sibling #1 — TotalSiblings=2
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork1 = await store.LoadThreadAsync("test-session", "fork-1");

        // Main is sibling #0 in the group it spawned
        Assert.Equal(2, reloadedMain!.TotalSiblings);
        Assert.Equal(0, reloadedMain.SiblingIndex);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Null(reloadedMain.PreviousSiblingId);
        Assert.Equal("fork-1", reloadedMain.NextSiblingId);

        // Fork-1 is sibling #1
        Assert.Equal(2, reloadedFork1!.TotalSiblings);
        Assert.Equal(1, reloadedFork1.SiblingIndex);
        Assert.False(reloadedFork1.IsOriginal);
        Assert.Equal("main", reloadedFork1.PreviousSiblingId);
        Assert.Null(reloadedFork1.NextSiblingId);
    }

    [Fact]
    public async Task Test10_ForkThread_PersistsThreadMetadata()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(
            main,
            "fork-with-metadata",
            fromMessageId: main.Messages[0].MessageId!,
            metadata: new Dictionary<string, object>
            {
                ["workspaceId"] = "hpdos-main",
                ["paneId"] = "chat-left"
            });

        // Assert
        var reloaded = await store.LoadThreadAsync("test-session", "fork-with-metadata");
        Assert.NotNull(reloaded);
        Assert.Equal("hpdos-main", reloaded.Metadata["workspaceId"]);
        Assert.Equal("chat-left", reloaded.Metadata["paneId"]);
    }

    [Fact]
    public async Task ForkThreadAsync_WithoutMessageId_ForksFromLatestMessage()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(AssistantMessage("Response 1"));
        main.AddMessage(UserMessage("Message 2"));
        await store.SaveInitialThreadAsync("test-session", main);

        var expectedForkPoint = main.Messages.Last().MessageId;

        // Act
        var forkId = await agent.ForkThreadAsync("test-session", "main", "latest-fork");

        // Assert
        Assert.Equal("latest-fork", forkId);

        var fork = await store.LoadThreadAsync("test-session", "latest-fork");
        Assert.NotNull(fork);
        Assert.Equal(expectedForkPoint, fork.ForkedAtMessageId);
        Assert.Equal(main.Messages.Count, fork.Messages.Count);
        Assert.Equal(main.Messages.Select(m => m.MessageId), fork.Messages.Select(m => m.MessageId));
    }

    [Fact]
    public async Task ForkThreadAsync_WithoutMessageId_ThrowsWhenThreadHasNoMessages()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        await store.SaveInitialThreadAsync("test-session", main);

        // Act
        var act = () => agent.ForkThreadAsync("test-session", "main", "empty-fork");

        // Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("has no messages to fork from", ex.Message);
    }

    [Fact]
    public async Task ForkThread_InvokesBeforeThreadForkCommit_WithPreparedTargetThread()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var middleware = new RecordingThreadForkMiddleware();
        var agent = await CreateAgentWithStore(store, middleware);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        var message = UserMessage("Message 1");
        message.MessageId = "message-1";
        main.AddMessage(message);
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(
            main,
            "fork-with-hook",
            fromMessageId: main.Messages[0].MessageId!,
            metadata: new Dictionary<string, object> { ["caller"] = "present" });

        // Assert
        var reloaded = await store.LoadThreadAsync("test-session", "fork-with-hook");
        Assert.NotNull(reloaded);
        Assert.Equal(1, middleware.CallCount);
        Assert.Equal("main", middleware.SourceThreadId);
        Assert.Equal("fork-with-hook", middleware.TargetThreadId);
        Assert.Equal("fork-with-hook", middleware.ActiveThreadId);
        Assert.Equal(0, middleware.ForkedAtMessageIndex);
        Assert.Equal("message-1", middleware.ForkedAtMessageId);
        Assert.True(middleware.CallerMetadataWasPresent);
        Assert.Equal("from-hook", reloaded!.Metadata["threadHook"]);
        Assert.Equal(2, reloaded.Messages.Count);
        Assert.Equal("hook-added", reloaded.Messages[1].MessageId);
    }

    [Fact]
    public async Task ForkThread_FlushesMiddlewareStateUpdatedByBeforeThreadForkCommit()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store, new ThreadForkStateMiddleware());
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(main, "fork-with-state", fromMessageId: main.Messages[0].MessageId!);

        // Assert
        var reloaded = await store.LoadThreadAsync("test-session", "fork-with-state");
        Assert.NotNull(reloaded);

        var state = MiddlewareState.LoadFromThread(reloaded, agent.StateFactories);
        var compactionState = state.GetState<CompactionStateData>(typeof(CompactionStateData).FullName!);
        Assert.NotNull(compactionState);
        Assert.Equal(42, compactionState!.MessageTurnCount);
    }

    [Fact]
    public async Task ForkThread_WhenBeforeThreadForkCommitThrows_DoesNotPersistTargetOrSourceTreeUpdates()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store, new ThrowingThreadForkMiddleware());
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.ForkThreadAsync(main, "fork-fails", fromMessageId: main.Messages[0].MessageId!));

        // Assert
        Assert.Equal("thread fork hook failed", ex.Message);
        Assert.Null(await store.LoadThreadAsync("test-session", "fork-fails"));

        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        Assert.NotNull(reloadedMain);
        Assert.Empty(reloadedMain!.ChildThreads);
        Assert.Equal(1, reloadedMain.TotalSiblings);
        Assert.Null(reloadedMain.NextSiblingId);
    }

    [Fact]
    public async Task ForkThread_WithCompactionOnFork_CompactsOnlyTargetThread()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 2);
        var agent = await CreateAgentWithStore(
            store,
            new CompactionMiddleware
            {
                Strategy = strategy,
                Config = new CompactionConfig
                {
                    Enabled = true,
                    CompactOnFork = true,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 2 }
                }
            });
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        for (var i = 0; i < 5; i++)
        {
            var message = UserMessage($"Message {i}");
            message.MessageId = $"message-{i}";
            main.AddMessage(message);
        }
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(main, "fork-compacted", fromMessageId: main.Messages[4].MessageId!);

        // Assert
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork = await store.LoadThreadAsync("test-session", "fork-compacted");

        reloadedMain!.Messages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2", "message-3", "message-4");
        reloadedFork!.Messages.Select(m => m.MessageId)
            .Should().Equal("message-3", "message-4");

        var state = MiddlewareState.LoadFromThread(reloadedFork, agent.StateFactories);
        var compactionState = state.GetState<CompactionStateData>(typeof(CompactionStateData).FullName!);
        compactionState.Should().NotBeNull();
        compactionState!.LastCompaction!.ModelCompactedMessageIds
            .Should().Equal("message-0", "message-1", "message-2");
    }

    [Fact]
    public async Task ForkThread_WithCompactionOnForkDisabled_DoesNotInvokeCompactionStrategy()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 1);
        var agent = await CreateAgentWithStore(
            store,
            new CompactionMiddleware
            {
                Strategy = strategy,
                Config = new CompactionConfig
                {
                    Enabled = true,
                    CompactOnFork = false
                }
            });
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        var message1 = UserMessage("Message 1");
        message1.MessageId = "message-1";
        main.AddMessage(message1);
        var message2 = UserMessage("Message 2");
        message2.MessageId = "message-2";
        main.AddMessage(message2);
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(main, "fork-uncompacted", fromMessageId: main.Messages[1].MessageId!);

        // Assert
        strategy.CallCount.Should().Be(0);
        var reloadedFork = await store.LoadThreadAsync("test-session", "fork-uncompacted");
        reloadedFork!.Messages.Select(m => m.MessageId)
            .Should().Equal("message-1", "message-2");
    }

    [Fact]
    public async Task ForkThread_WithCompactionIntentEnabled_CompactsEvenWhenGlobalForkCompactionIsDisabled()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 1);
        var agent = await CreateAgentWithStore(
            store,
            new CompactionMiddleware
            {
                Strategy = strategy,
                Config = new CompactionConfig
                {
                    Enabled = true,
                    CompactOnFork = false,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 1 }
                }
            });
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        for (var i = 0; i < 3; i++)
        {
            var message = UserMessage($"Message {i}");
            message.MessageId = $"message-{i}";
            main.AddMessage(message);
        }
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(
            main,
            "fork-force-compacted",
            fromMessageId: main.Messages[2].MessageId!,
            forkOptions: new ThreadForkOptions
            {
                CompactionIntent = ThreadForkCompactionIntent.Enabled
            });

        // Assert
        strategy.CallCount.Should().Be(1);
        var reloadedFork = await store.LoadThreadAsync("test-session", "fork-force-compacted");
        reloadedFork!.Messages.Select(m => m.MessageId)
            .Should().Equal("message-2");
    }

    [Fact]
    public async Task ForkThread_WithCompactionIntentDisabled_SkipsGlobalForkCompaction()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 1);
        var agent = await CreateAgentWithStore(
            store,
            new CompactionMiddleware
            {
                Strategy = strategy,
                Config = new CompactionConfig
                {
                    Enabled = true,
                    CompactOnFork = true,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 1 }
                }
            });
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        for (var i = 0; i < 3; i++)
        {
            var message = UserMessage($"Message {i}");
            message.MessageId = $"message-{i}";
            main.AddMessage(message);
        }
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        await agent.ForkThreadAsync(
            main,
            "fork-force-uncompacted",
            fromMessageId: main.Messages[2].MessageId!,
            forkOptions: new ThreadForkOptions
            {
                CompactionIntent = ThreadForkCompactionIntent.Disabled
            });

        // Assert
        strategy.CallCount.Should().Be(0);
        var reloadedFork = await store.LoadThreadAsync("test-session", "fork-force-uncompacted");
        reloadedFork!.Messages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
    }

    [Fact]
    public async Task ForkThread_WhenMessageIdWasCompactedOut_ThrowsWithReplacementCandidates()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        var removed = UserMessage("Removed");
        removed.MessageId = "removed-message";
        main.AddMessage(removed);
        var retained = UserMessage("Retained");
        retained.MessageId = "retained-message";
        main.AddMessage(retained);
        await store.SaveInitialThreadAsync("test-session", main);

        var summary = new ChatMessage(ChatRole.Assistant, "Summary")
        {
            MessageId = "summary-message"
        };
        await store.AppendThreadEventAsync(
            "test-session",
            "main",
            ThreadEventFactory.ThreadHistoryCompacted(
                "test-session",
                "main",
                new ThreadHistoryCompactedEvent(
                    "compaction-1",
                    ["removed-message"],
                    ["removed-message"],
                    [summary],
                    nameof(MessageCountingCompactionOptions),
                    nameof(CompactThreadHistoryOptions),
                    nameof(ExactCompactedMessagesBoundaryOptions),
                    "Summary",
                    DateTimeOffset.UtcNow)));

        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        reloadedMain!.Session = session;

        // Act
        var ex = await Assert.ThrowsAsync<MessageNotPresentOnThreadException>(
            () => agent.ForkThreadAsync(reloadedMain, "fork-missing", fromMessageId: "removed-message"));

        // Assert
        ex.ReplacementMessageIds.Should().ContainSingle("summary-message");
        Assert.Null(await store.LoadThreadAsync("test-session", "fork-missing"));
    }

    [Fact]
    public async Task Test11_ForkThread_SetsCorrectSiblingIndex_ChronologicalOrder()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(AssistantMessage("Response 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act - Fork main THREE TIMES at the SAME index
        // This creates three siblings: fork-1, fork-2, fork-3
        // All have ForkedFrom="main", ForkedAtMessageIndex=1
        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[1].MessageId!);
        await Task.Delay(10); // Ensure time difference

        // Reload main to get updated metadata
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        var fork2 = await agent.ForkThreadAsync(main, "fork-2", fromMessageId: main.Messages[1].MessageId!);
        await Task.Delay(10);

        // Reload main again
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        var fork3 = await agent.ForkThreadAsync(main, "fork-3", fromMessageId: main.Messages[1].MessageId!);

        // Assert - main is sibling #0, fork-1 is #1, fork-2 is #2, fork-3 is #3 (TotalSiblings=4)
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork1 = await store.LoadThreadAsync("test-session", "fork-1");
        var reloadedFork2 = await store.LoadThreadAsync("test-session", "fork-2");
        var reloadedFork3 = await store.LoadThreadAsync("test-session", "fork-3");

        // main is always slot 0
        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(4, reloadedMain.TotalSiblings);
        Assert.True(reloadedMain.IsOriginal);

        // Forks are ordered chronologically: main(0), fork-1(1), fork-2(2), fork-3(3)
        Assert.Equal(1, reloadedFork1!.SiblingIndex);
        Assert.Equal(2, reloadedFork2!.SiblingIndex);
        Assert.Equal(3, reloadedFork3!.SiblingIndex);

        // All have total = 4
        Assert.Equal(4, reloadedFork1.TotalSiblings);
        Assert.Equal(4, reloadedFork2.TotalSiblings);
        Assert.Equal(4, reloadedFork3.TotalSiblings);

        // Forks are not original
        Assert.False(reloadedFork1.IsOriginal);
        Assert.False(reloadedFork2.IsOriginal);
        Assert.False(reloadedFork3.IsOriginal);
    }

    [Fact]
    public async Task Test11_ForkThread_LinksNavigationPointers_Bidirectionally()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);

        // Assert - main is sibling #0, fork-1 is sibling #1 — linked bidirectionally
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork1 = await store.LoadThreadAsync("test-session", "fork-1");

        // main: first in group, points forward to fork-1
        Assert.Null(reloadedMain!.PreviousSiblingId);
        Assert.Equal("fork-1", reloadedMain.NextSiblingId);

        // fork-1: last in group, points back to main
        Assert.Equal("main", reloadedFork1!.PreviousSiblingId);
        Assert.Null(reloadedFork1.NextSiblingId);
    }

    [Fact]
    public async Task Test12_ForkThread_UpdatesParentChildThreads_List()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act
        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);

        // Assert - Parent tracks child
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        Assert.Contains("fork-1", reloadedMain!.ChildThreads);
        Assert.Equal(1, reloadedMain.TotalForks);

        // Child references parent
        Assert.Equal("main", fork1.ForkedFrom);
    }

    //──────────────────────────────────────────────────────────────────
    // INTEGRATION TESTS - SIBLING REDESIGN (source = slot 0)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task N1_SourceThread_IsSlotZero_AfterFirstFork()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act - create one fork
        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);

        // Assert - main is sibling #0, fork-1 is sibling #1
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork1 = await store.LoadThreadAsync("test-session", "fork-1");

        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(2, reloadedMain.TotalSiblings);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Null(reloadedMain.PreviousSiblingId);
        Assert.Equal("fork-1", reloadedMain.NextSiblingId);

        Assert.Equal(1, reloadedFork1!.SiblingIndex);
        Assert.Equal(2, reloadedFork1.TotalSiblings);
        Assert.False(reloadedFork1.IsOriginal);
        Assert.Equal("main", reloadedFork1.PreviousSiblingId);
        Assert.Null(reloadedFork1.NextSiblingId);
    }

    [Fact]
    public async Task N2_ForkTwiceFromSameSource_SourceRemainsSlotZero()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act - fork twice at the same index
        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);
        await Task.Delay(10);
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-2", fromMessageId: main.Messages[0].MessageId!);

        // Assert - main(0), fork-1(1), fork-2(2) — TotalSiblings=3 on all
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork1 = await store.LoadThreadAsync("test-session", "fork-1");
        var reloadedFork2 = await store.LoadThreadAsync("test-session", "fork-2");

        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(3, reloadedMain.TotalSiblings);
        Assert.True(reloadedMain.IsOriginal);

        Assert.Equal(1, reloadedFork1!.SiblingIndex);
        Assert.Equal(3, reloadedFork1.TotalSiblings);
        Assert.False(reloadedFork1.IsOriginal);

        Assert.Equal(2, reloadedFork2!.SiblingIndex);
        Assert.Equal(3, reloadedFork2.TotalSiblings);
        Assert.False(reloadedFork2.IsOriginal);
    }

    [Fact]
    public async Task N3_ForkAtDifferentMessageIndices_IndependentGroups()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 0"));
        main.AddMessage(AssistantMessage("Response 0"));
        main.AddMessage(UserMessage("Message 2"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act - fork at index 0 and at index 2 (independent sibling groups)
        await agent.ForkThreadAsync(main, "fork-at-0", fromMessageId: main.Messages[0].MessageId!);
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-at-2", fromMessageId: main.Messages[2].MessageId!);

        // Assert - two forks with different ForkedAtMessageIndex are separate groups
        var forkAt0 = await store.LoadThreadAsync("test-session", "fork-at-0");
        var forkAt2 = await store.LoadThreadAsync("test-session", "fork-at-2");

        Assert.Equal(0, forkAt0!.ForkedAtMessageIndex);
        Assert.Equal(2, forkAt2!.ForkedAtMessageIndex);
        Assert.Equal("main", forkAt0.ForkedFrom);
        Assert.Equal("main", forkAt2.ForkedFrom);

        // Each is sibling #1 in its own group (main is #0 in both)
        Assert.Equal(1, forkAt0.SiblingIndex);
        Assert.Equal(1, forkAt2.SiblingIndex);
        Assert.Equal(2, forkAt0.TotalSiblings);
        Assert.Equal(2, forkAt2.TotalSiblings);
    }

    [Fact]
    public async Task N4_ThirdForkFromSamePoint_PreviousForkIndexStable()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act - create 3 forks; verify existing forks keep stable indices
        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);
        await Task.Delay(10);
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-2", fromMessageId: main.Messages[0].MessageId!);
        await Task.Delay(10);
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-3", fromMessageId: main.Messages[0].MessageId!);

        // Assert - fork-1 stays at index 1 (never re-ordered)
        var f1 = await store.LoadThreadAsync("test-session", "fork-1");
        var f2 = await store.LoadThreadAsync("test-session", "fork-2");
        var f3 = await store.LoadThreadAsync("test-session", "fork-3");

        Assert.Equal(1, f1!.SiblingIndex);
        Assert.Equal(2, f2!.SiblingIndex);
        Assert.Equal(3, f3!.SiblingIndex);
        Assert.Equal(4, f1.TotalSiblings);
        Assert.Equal(4, f2.TotalSiblings);
        Assert.Equal(4, f3.TotalSiblings);
    }

    [Fact]
    public async Task N5_OriginalThreadId_IsSourceThreadId_ForAllForks()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync("test-session", main);
        main.Session = session;

        // Act - two forks from main
        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);
        main = (await store.LoadThreadAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-2", fromMessageId: main.Messages[0].MessageId!);

        // Assert - source (main) has no OriginalThreadId; forks point to main
        var reloadedMain = await store.LoadThreadAsync("test-session", "main");
        var reloadedFork1 = await store.LoadThreadAsync("test-session", "fork-1");
        var reloadedFork2 = await store.LoadThreadAsync("test-session", "fork-2");

        Assert.Null(reloadedMain!.OriginalThreadId);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Equal("main", reloadedFork1!.OriginalThreadId);
        Assert.Equal("main", reloadedFork2!.OriginalThreadId);
        Assert.False(reloadedFork1.IsOriginal);
        Assert.False(reloadedFork2.IsOriginal);
    }

    //──────────────────────────────────────────────────────────────────
    // HELPER METHODS
    //──────────────────────────────────────────────────────────────────

    private async Task<HPD.Agent.Agent> CreateAgentWithStore(
        ISessionStore store,
        params IAgentMiddleware[] middlewares)
    {
        var builder = new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store);

        foreach (var middleware in middlewares)
        {
            builder.WithMiddleware(middleware);
        }

        return await builder.BuildAsync(CancellationToken.None);
    }

    private sealed class RecordingThreadForkMiddleware : IAgentMiddleware
    {
        public int CallCount { get; private set; }
        public string? SourceThreadId { get; private set; }
        public string? TargetThreadId { get; private set; }
        public string? ActiveThreadId { get; private set; }
        public int ForkedAtMessageIndex { get; private set; }
        public string? ForkedAtMessageId { get; private set; }
        public bool CallerMetadataWasPresent { get; private set; }

        public Task BeforeThreadForkCommitAsync(
            BeforeThreadForkCommitContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SourceThreadId = context.SourceThread.Id;
            TargetThreadId = context.TargetThread.Id;
            ActiveThreadId = context.Thread?.Id;
            ForkedAtMessageIndex = context.ForkedAtMessageIndex;
            ForkedAtMessageId = context.ForkedAtMessageId;
            CallerMetadataWasPresent = context.TargetThread.Metadata.ContainsKey("caller");

            context.TargetThread.Metadata["threadHook"] = "from-hook";
            context.TargetThread.Messages.Add(new ChatMessage(ChatRole.Assistant, "hook-added")
            {
                MessageId = "hook-added"
            });

            return Task.CompletedTask;
        }
    }

    private sealed class ThreadForkStateMiddleware : IAgentMiddleware
    {
        public Task BeforeThreadForkCommitAsync(
            BeforeThreadForkCommitContext context,
            CancellationToken cancellationToken)
        {
            context.UpdateMiddlewareState<CompactionStateData>(
                state => state with { MessageTurnCount = 42 });

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingThreadForkMiddleware : IAgentMiddleware
    {
        public Task BeforeThreadForkCommitAsync(
            BeforeThreadForkCommitContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("thread fork hook failed");
    }

    private sealed class RetainLastMessagesCompactionStrategy(int retainCount) : ICompactionStrategy
    {
        public int CallCount { get; private set; }

        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var modelVisible = originalMessages.TakeLast(retainCount).ToList();
            return Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                modelVisible,
                new MessageCountingCompactionOptions { TargetMessageCount = retainCount }));
        }
    }
}
