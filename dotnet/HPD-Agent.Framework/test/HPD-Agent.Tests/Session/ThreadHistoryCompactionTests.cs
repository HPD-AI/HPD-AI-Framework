using FluentAssertions;
using HPD.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Session;

public class ThreadHistoryCompactionTests
{
    [Fact]
    public void Planner_PreserveThreadHistory_CreatesSoftCheckpointPlan()
    {
        var thread = CreateThread(5);
        var compaction = CreateCompaction(thread, compactedCount: 3);

        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new PreserveThreadHistoryOptions());

        plan.Should().NotBeNull();
        plan!.DurableCompactedMessages.Should().BeEmpty();
        plan.ModelCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
        plan.RetainedMessages.Select(m => m.MessageId)
            .Should().Equal("message-3", "message-4");
    }

    [Fact]
    public void Planner_ExactBoundary_SelectsOnlyModelCompactedMessages()
    {
        var thread = CreateThread(5);
        var compaction = CreateCompaction(thread, compactedCount: 3);

        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions());

        plan.Should().NotBeNull();
        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
    }

    [Fact]
    public void Planner_IncludePreviousBoundary_DoesNotRemoveRetainedMessages()
    {
        var thread = CreateThread(5);
        var compaction = CreateCompaction(thread, compactedCount: 2);

        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions
            {
                Boundary = new IncludePreviousMessagesBoundaryOptions(2)
            });

        plan.Should().NotBeNull();
        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1");
    }

    [Fact]
    public void Planner_MessageTurnBoundary_ExpandsToMessagesWithSameTurnMetadata()
    {
        var thread = CreateThread(4);
        thread.Messages[0].AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-a"
        };
        thread.Messages[1].AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-a"
        };
        thread.Messages[2].AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-b"
        };

        var compaction = CompactionResult.FromOriginalAndCompacted(
            thread.Messages,
            thread.Messages.Skip(2).ToList(),
            new MessageCountingCompactionOptions());

        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions { Boundary = new IncludeMessageTurnBoundaryOptions() });

        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1");
    }

    [Fact]
    public void Planner_ToolCallGroupBoundary_ExpandsToMatchingCallResult()
    {
        var thread = new Thread("session", "main");
        thread.AddMessage(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Lookup")])
        {
            MessageId = "assistant-call"
        });
        thread.AddMessage(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "done")])
        {
            MessageId = "tool-result"
        });
        thread.AddMessage(new ChatMessage(ChatRole.User, "next")
        {
            MessageId = "next-user"
        });

        var compaction = CompactionResult.FromOriginalAndCompacted(
            thread.Messages,
            thread.Messages.Skip(2).ToList(),
            new MessageCountingCompactionOptions());

        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions { Boundary = new IncludeToolCallGroupBoundaryOptions() });

        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("assistant-call", "tool-result");
    }

    [Fact]
    public async Task Compactor_CompactRetentionWithoutReplacements_RemovesDurableMessagesFromLiveThread()
    {
        var thread = CreateThread(5);
        var compaction = CreateCompaction(thread, compactedCount: 3);
        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions())!;

        var result = await new ThreadHistoryCompactor().CompactAsync(thread, plan, CancellationToken.None);

        result.DurableCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        result.ReplacementMessageIds.Should().BeEmpty();
        thread.Messages.Select(m => m.MessageId).Should().Equal("message-3", "message-4");
    }

    [Fact]
    public async Task Compactor_CompactRetention_InsertsReplacementMessages()
    {
        var thread = CreateThread(5);
        var compaction = CreateCompaction(thread, compactedCount: 3, includeSummary: true);
        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions())!;

        var result = await new ThreadHistoryCompactor().CompactAsync(thread, plan, CancellationToken.None);

        result.DurableCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        result.ReplacementMessageIds.Should().ContainSingle("summary");
        thread.Messages.Select(m => m.MessageId).Should().Equal("summary", "message-3", "message-4");
    }

    [Fact]
    public async Task Compactor_PreserveRetention_DoesNotMutateLiveThread()
    {
        var thread = CreateThread(5);
        var compaction = CreateCompaction(thread, compactedCount: 3, includeSummary: true);
        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new PreserveThreadHistoryOptions())!;

        var result = await new ThreadHistoryCompactor().CompactAsync(thread, plan, CancellationToken.None);

        result.ModelCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        result.DurableCompactedMessageIds.Should().BeEmpty();
        result.ReplacementMessageIds.Should().ContainSingle("summary");
        thread.Messages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2", "message-3", "message-4");
    }

    [Fact]
    public async Task Compactor_PreserveRetention_AppendsSoftCheckpointToStore()
    {
        var (store, thread) = await CreatePersistedThreadAsync(5);
        var compaction = CreateCompaction(thread, compactedCount: 3, includeSummary: true);
        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new PreserveThreadHistoryOptions())!;

        var result = await new ThreadHistoryCompactor().CompactAsync(thread, plan, CancellationToken.None);

        var document = await store.LoadThreadDocumentAsync(thread.SessionId, thread.Id);
        var checkpoint = document!.Events.OfType<ThreadHistoryCompactionCheckpointEvent>().Should().ContainSingle().Subject;
        checkpoint.Mode.Should().Be(ThreadHistoryCompactionMode.Soft);
        checkpoint.ModelCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        checkpoint.RetainedMessageIds.Should().Equal("message-3", "message-4");
        checkpoint.DurableCompactedMessageIds.Should().BeEmpty();
        checkpoint.ReplacementMessages.Select(message => message.MessageId).Should().ContainSingle("summary");
        result.CheckpointEvent.EventId.Should().Be(checkpoint.EventId);
        result.CheckpointEvent.SequenceNumber.Should().Be(checkpoint.SequenceNumber);
        result.CheckpointEvent.SessionId.Should().Be(checkpoint.SessionId);
        result.CheckpointEvent.ThreadId.Should().Be(checkpoint.ThreadId);

        var projected = await store.LoadThreadAsync(thread.SessionId, thread.Id);
        projected!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-0", "message-1", "message-2", "message-3", "message-4");
    }

    [Fact]
    public async Task Compactor_CompactRetention_AppendsHardCheckpointToStoreProjection()
    {
        var (store, thread) = await CreatePersistedThreadAsync(5);
        var compaction = CreateCompaction(thread, compactedCount: 3, includeSummary: true);
        var plan = new ThreadCompactionPlanner().Plan(
            thread,
            compaction,
            new CompactThreadHistoryOptions())!;

        await new ThreadHistoryCompactor().CompactAsync(thread, plan, CancellationToken.None);

        var document = await store.LoadThreadDocumentAsync(thread.SessionId, thread.Id);
        var checkpoint = document!.Events.OfType<ThreadHistoryCompactionCheckpointEvent>().Should().ContainSingle().Subject;
        checkpoint.Mode.Should().Be(ThreadHistoryCompactionMode.Hard);
        checkpoint.ModelCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        checkpoint.RetainedMessageIds.Should().Equal("message-3", "message-4");
        checkpoint.DurableCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        checkpoint.ReplacementMessages.Select(message => message.MessageId).Should().ContainSingle("summary");

        var projected = await store.LoadThreadAsync(thread.SessionId, thread.Id);
        projected!.Messages.Select(message => message.MessageId)
            .Should().Equal("summary", "message-3", "message-4");
    }

    [Fact]
    public void Projector_AppliesHardCompactionCheckpointEvent()
    {
        var events = new List<AgentEvent>
        {
            ThreadEventFactory.ThreadCreated(new Thread("session", "main")),
        };

        var thread = CreateThread(4);
        foreach (var message in thread.Messages)
        {
            events.Add(ThreadEventFactory.ContentAdded("session", "main", message, message.Contents[0]));
        }

        events.Add(ThreadEventFactory.ThreadHistoryCompactionCheckpoint(
            "session",
            "main",
            new ThreadHistoryCompactionCheckpointEvent(
                "compaction",
                ["message-0", "message-1"],
                ["message-2", "message-3"],
                ["message-0", "message-1"],
                [new ChatMessage(ChatRole.Assistant, "summary") { MessageId = "summary" }],
                nameof(SummarizingCompactionOptions),
                nameof(CompactThreadHistoryOptions),
                nameof(ExactCompactedMessagesBoundaryOptions),
                "summary",
                DateTimeOffset.UtcNow,
                ThreadHistoryCompactionMode.Hard)));

        var projected = ThreadProjector.Project("session", "main", events);

        projected.Messages.Select(m => m.MessageId)
            .Should().Equal("summary", "message-2", "message-3");
        projected.Messages[0].Text.Should().Be("summary");
    }

    [Fact]
    public void Projector_LeavesMessagesForSoftCompactionCheckpointEvent()
    {
        var events = new List<AgentEvent>
        {
            ThreadEventFactory.ThreadCreated(new Thread("session", "main")),
        };

        var thread = CreateThread(4);
        foreach (var message in thread.Messages)
        {
            events.Add(ThreadEventFactory.ContentAdded("session", "main", message, message.Contents[0]));
        }

        events.Add(ThreadEventFactory.ThreadHistoryCompactionCheckpoint(
            "session",
            "main",
            new ThreadHistoryCompactionCheckpointEvent(
                "compaction",
                ["message-0", "message-1"],
                ["message-2", "message-3"],
                [],
                [new ChatMessage(ChatRole.Assistant, "summary") { MessageId = "summary" }],
                nameof(SummarizingCompactionOptions),
                nameof(PreserveThreadHistoryOptions),
                nameof(ExactCompactedMessagesBoundaryOptions),
                "summary",
                DateTimeOffset.UtcNow,
                ThreadHistoryCompactionMode.Soft)));

        var projected = ThreadProjector.Project("session", "main", events);

        projected.Messages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2", "message-3");
    }

    private static Thread CreateThread(int messageCount)
    {
        var thread = new Thread("session", "main");
        for (var i = 0; i < messageCount; i++)
        {
            thread.AddMessage(new ChatMessage(ChatRole.User, $"Message {i}")
            {
                MessageId = $"message-{i}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return thread;
    }

    private static async Task<(InMemorySessionStore Store, Thread Thread)> CreatePersistedThreadAsync(
        int messageCount)
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session") { Store = store };
        var thread = session.CreateThread("main");

        for (var i = 0; i < messageCount; i++)
        {
            thread.AddMessage(new ChatMessage(ChatRole.User, $"Message {i}")
            {
                MessageId = $"message-{i}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync(session.Id, thread);
        return (store, thread);
    }

    private static CompactionResult CreateCompaction(
        Thread thread,
        int compactedCount,
        bool includeSummary = false)
    {
        var retained = thread.Messages.Skip(compactedCount).ToList();
        var visible = includeSummary
            ? new[] { new ChatMessage(ChatRole.Assistant, "summary") { MessageId = "summary" } }.Concat(retained).ToList()
            : retained;

        return CompactionResult.FromOriginalAndCompacted(
            thread.Messages,
            visible,
            includeSummary ? new SummarizingCompactionOptions() : new MessageCountingCompactionOptions());
    }
}
