using FluentAssertions;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using Xunit;

namespace HPD.Agent.Tests.Session;

public sealed class ThreadCompactionEngineTests
{
    [Fact]
    public async Task Prepare_DoesNotMutateThreadBeforeCommit()
    {
        var thread = CreateThread(4);
        var original = thread.Messages.Select(static message => message.MessageId).ToArray();
        var engine = new ThreadCompactionEngine();

        var prepared = await engine.PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, null),
            RemoveAtHead(CompactionCommitMode.Soft));

        prepared.Should().NotBeNull();
        thread.Messages.Select(static message => message.MessageId).Should().Equal(original);
    }

    [Fact]
    public async Task CurrentHead_WithNoPreservation_SelectsEntireHistory()
    {
        var thread = CreateThread(4);
        var engine = new ThreadCompactionEngine();

        var prepared = await engine.PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, null),
            RemoveAtHead(CompactionCommitMode.Soft));

        prepared!.CompactedMessageIds.Should().Equal("message-0", "message-1", "message-2", "message-3");
        prepared.ResultingMessages.Should().BeEmpty();
        prepared.Checkpoint.CommitMode.Should().Be(CompactionCommitMode.Soft);
    }

    [Fact]
    public async Task PreservePreviousTurns_KeepsConfiguredSemanticTail()
    {
        var thread = CreateThread(6);
        var engine = new ThreadCompactionEngine();
        var specification = RemoveAtHead(CompactionCommitMode.Soft) with
        {
            Preservation = new PreservePreviousTurns(2)
        };

        var prepared = await engine.PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, null),
            specification);

        prepared!.CompactedMessageIds.Should().Equal("message-0", "message-1");
        prepared.PreservedMessageIds.Should().Equal("message-2", "message-3", "message-4", "message-5");
        prepared.ResultingMessages.Select(static message => message.MessageId)
            .Should().Equal("message-2", "message-3", "message-4", "message-5");
    }

    [Fact]
    public async Task CompactAtMessage_UsesContainingTurnAsExclusiveCutPoint()
    {
        var thread = CreateThread(5);
        var engine = new ThreadCompactionEngine();
        var specification = RemoveAtHead(CompactionCommitMode.Soft) with
        {
            Point = new CompactAtMessage("message-3")
        };

        var prepared = await engine.PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, null),
            specification);

        prepared!.CompactedMessageIds.Should().Equal("message-0", "message-1");
        prepared.AfterPointMessageIds.Should().Equal("message-2", "message-3", "message-4");
    }

    [Fact]
    public async Task SoftCommit_AppliesPreparedProjection()
    {
        var thread = CreateThread(4);
        var engine = new ThreadCompactionEngine();
        var context = new ThreadCompactionContext(thread, thread.Messages, null, null);
        var specification = RemoveAtHead(CompactionCommitMode.Soft) with
        {
            Preservation = new PreservePreviousTurns(1)
        };
        var prepared = await engine.PrepareAsync(context, specification);

        var result = await engine.CommitAsync(context, prepared!);

        result.CommittedCheckpoint.Should().BeSameAs(prepared!.Checkpoint);
        thread.Messages.Select(static message => message.MessageId).Should().Equal("message-2", "message-3");
    }

    [Fact]
    public async Task HardCommit_ReplacesEverySourceEventWithConfiguredResult()
    {
        var thread = CreateThread(4);
        var store = new InMemorySessionStore();
        var key = new ThreadKey(thread.SessionId, thread.Id);
        var sourceEvents = new List<AgentEvent> { ThreadEventFactory.ThreadCreated(thread) };
        foreach (var message in thread.Messages)
            sourceEvents.AddRange(ThreadMessageEventConverter.ToThreadEvents(thread.SessionId, thread.Id, message));
        var append = await store.AppendThreadEventsAsync(key, sourceEvents);
        var oldEventIds = append.CommittedEvents.Select(static evt => evt.EventId).ToHashSet(StringComparer.Ordinal);
        var publisher = new ThreadEventPublisher(store, new EventCoordinator());
        var context = new ThreadCompactionContext(thread, thread.Messages, publisher, null);
        var specification = RemoveAtHead(CompactionCommitMode.Hard) with
        {
            Preservation = new PreservePreviousTurns(1)
        };

        var prepared = await new ThreadCompactionEngine().PrepareAsync(context, specification);
        await new ThreadCompactionEngine().CommitAsync(context, prepared!);

        var replay = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(2))))
            replay.AddRange(batch.Events);
        replay.Should().NotContain(evt => oldEventIds.Contains(evt.EventId));
        replay.Should().ContainSingle(evt => evt is ThreadCreatedEvent);
        replay.Should().ContainSingle(evt => evt is ThreadHistoryCompactionCheckpointEvent);
        var projected = await store.ProjectThreadAsync(thread.SessionId, thread.Id, ThreadProjectionPurpose.ThreadHistory);
        projected!.Messages.Select(static message => message.MessageId).Should().Equal("message-2", "message-3");
    }

    [Fact]
    public async Task Execute_SoftCommitsCompleteLifecycleAroundCheckpoint()
    {
        var thread = CreateThread(4);
        var store = new InMemorySessionStore();
        var key = new ThreadKey(thread.SessionId, thread.Id);
        await store.AppendThreadEventsAsync(key, ThreadJournalEncoder.Encode(thread, thread.Messages));
        var context = new ThreadCompactionContext(
            thread,
            thread.Messages,
            new ThreadEventPublisher(store, new EventCoordinator()),
            null);

        var result = await new ThreadCompactionEngine().ExecuteAsync(
            context,
            RemoveAtHead(CompactionCommitMode.Soft),
            "agent",
            2,
            CompactionOrigin.Automatic,
            CompactionContinuation.Continue);

        result.TerminalEvent.Status.Should().Be(CompactionStatus.Completed);
        var replay = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(1))))
            replay.AddRange(batch.Events);
        replay.OfType<CompactionEvent>().Select(static evt => evt.Status)
            .Should().Equal(CompactionStatus.Started, CompactionStatus.Completed);
        replay.Should().ContainSingle(evt => evt is ThreadHistoryCompactionCheckpointEvent);
    }

    [Fact]
    public async Task Execute_HardDropsSourceLifecycleAndCommitsCompletedInNewGeneration()
    {
        var thread = CreateThread(4);
        var store = new InMemorySessionStore();
        var key = new ThreadKey(thread.SessionId, thread.Id);
        await store.AppendThreadEventsAsync(key, ThreadJournalEncoder.Encode(thread, thread.Messages));
        var context = new ThreadCompactionContext(
            thread,
            thread.Messages,
            new ThreadEventPublisher(store, new EventCoordinator()),
            null);

        await new ThreadCompactionEngine().ExecuteAsync(
            context,
            RemoveAtHead(CompactionCommitMode.Hard) with
            {
                Preservation = new PreservePreviousTurns(1)
            },
            "agent",
            0,
            CompactionOrigin.Explicit,
            CompactionContinuation.StopAfterCompaction);

        var head = await store.GetThreadEventHeadAsync(key);
        head!.Cursor.Generation.Should().Be(2);
        var replay = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(2))))
            replay.AddRange(batch.Events);
        replay.OfType<CompactionEvent>().Should().ContainSingle()
            .Which.Status.Should().Be(CompactionStatus.Completed);
        replay.Should().ContainSingle(evt => evt is ThreadHistoryCompactionCheckpointEvent);
    }

    [Fact]
    public async Task HardCommit_ReencodesAuthoritativeControlSeedsInReplacementGeneration()
    {
        var thread = CreateThread(4);
        var store = new InMemorySessionStore();
        var key = new ThreadKey(thread.SessionId, thread.Id);
        await store.AppendThreadEventsAsync(key, ThreadJournalEncoder.Encode(thread, thread.Messages));
        var seed = new ThreadExecutionStartedEvent("active-run", "agent", DateTimeOffset.UtcNow);
        var context = new ThreadCompactionContext(
            thread,
            thread.Messages,
            new ThreadEventPublisher(store, new EventCoordinator()),
            null,
            new StaticSeedProvider(seed));
        var engine = new ThreadCompactionEngine();
        var prepared = await engine.PrepareAsync(
            context,
            RemoveAtHead(CompactionCommitMode.Hard) with
            {
                Preservation = new PreservePreviousTurns(1)
            });

        await engine.CommitAsync(context, prepared!);

        var replay = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(2))))
            replay.AddRange(batch.Events);
        var committedSeed = replay.OfType<ThreadExecutionStartedEvent>().Should().ContainSingle().Subject;
        committedSeed.ThreadExecutionId.Should().Be("active-run");
        committedSeed.ThreadSequenceNumber.Should().BePositive();
    }

    [Fact]
    public async Task CompactAtMessage_RejectsAStaleJournalGeneration()
    {
        var thread = CreateThread(4);
        var store = new InMemorySessionStore();
        var key = new ThreadKey(thread.SessionId, thread.Id);
        await store.AppendThreadEventsAsync(key, ThreadJournalEncoder.Encode(thread, thread.Messages));
        var publisher = new ThreadEventPublisher(store, new EventCoordinator());
        var context = new ThreadCompactionContext(thread, thread.Messages, publisher, null);
        var specification = RemoveAtHead(CompactionCommitMode.Soft) with
        {
            Point = new CompactAtMessage("message-2", ExpectedJournalGeneration: 7)
        };

        var action = async () => await new ThreadCompactionEngine().PrepareAsync(context, specification);

        var conflict = await action.Should().ThrowAsync<ThreadCursorConflictException>();
        conflict.Which.Cursor.Generation.Should().Be(7);
        conflict.Which.Head.Generation.Should().Be(1);
    }

    [Fact]
    public async Task SummarizingCompaction_UsesAnIsolatedTextOnlyModelRequest()
    {
        var thread = new Thread("session", "thread", "test-agent");
        thread.Messages.Add(new ChatMessage(ChatRole.System, "You are the coding agent."));
        thread.Messages.Add(new ChatMessage(ChatRole.User, "Inspect the workspace."));
        thread.Messages.Add(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>())]));
        thread.Messages.Add(new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-1", "file contents")]));
        thread.Messages.Add(new ChatMessage(
            ChatRole.Assistant,
            [new TestInputRequestContent("input-1")]));
        thread.Messages.Add(new ChatMessage(
            ChatRole.User,
            [new TestInputResponseContent("input-1")]));
        thread.Messages.Add(new ChatMessage(ChatRole.Assistant, "The workspace uses .NET."));
        foreach (var (message, index) in thread.Messages.Select((message, index) => (message, index)))
            message.MessageId = $"message-{index}";
        var client = new CapturingChatClient(new ChatMessage(ChatRole.Assistant, "Continuation handoff"));

        var prepared = await new ThreadCompactionEngine().PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, client),
            SummarizeAtHead());

        prepared.Should().NotBeNull();
        client.Options.Should().NotBeNull();
        client.Options!.ToolMode.Should().Be(ChatToolMode.None);
        client.Options.Tools.Should().BeEmpty();
        client.Messages.Should().HaveCount(3);
        client.Messages[0].Text.Should().Be("Inspect the workspace.");
        client.Messages[1].Text.Should().Be("The workspace uses .NET.");
        client.Messages[2].Role.Should().Be(ChatRole.System);
        client.Messages.SelectMany(static message => message.Contents)
            .Should().NotContain(content =>
                content.GetType() == typeof(FunctionCallContent) ||
                content.GetType() == typeof(FunctionResultContent) ||
                content.GetType() == typeof(TestInputRequestContent) ||
                content.GetType() == typeof(TestInputResponseContent));
        prepared!.ResultingMessages.Should().ContainSingle().Which.Text.Should().Be("Continuation handoff");
    }

    [Fact]
    public async Task SummarizingCompaction_IncorporatesAPreviousHandoff()
    {
        var thread = new Thread("session", "thread", "test-agent");
        thread.Messages.Add(new ChatMessage(ChatRole.Assistant, "Earlier continuation handoff")
        {
            MessageId = "summary-1"
        });
        thread.Messages.Add(new ChatMessage(ChatRole.User, "Continue the implementation")
        {
            MessageId = "message-1"
        });
        var client = new CapturingChatClient(new ChatMessage(ChatRole.Assistant, "Updated continuation handoff"));

        var prepared = await new ThreadCompactionEngine().PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, client),
            SummarizeAtHead());

        client.Messages[0].Text.Should().Be("Earlier continuation handoff");
        client.Messages[1].Text.Should().Be("Continue the implementation");
        client.Messages[^1].Role.Should().Be(ChatRole.System);
        prepared!.ResultingMessages.Should().ContainSingle().Which.Text.Should().Be("Updated continuation handoff");
    }

    [Fact]
    public async Task SummarizingCompaction_PropagatesCancellationToTheClient()
    {
        var thread = CreateThread(2);
        var client = new CapturingChatClient(new ChatMessage(ChatRole.Assistant, "unused"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () => await new ThreadCompactionEngine().PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, client),
            SummarizeAtHead(),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        client.CancellationToken.Should().Be(cancellation.Token);
        thread.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task SummarizingCompaction_RejectsAStructuredToolRequest()
    {
        var thread = CreateThread(2);
        var client = new CapturingChatClient(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>())]));

        var action = async () => await new ThreadCompactionEngine().PrepareAsync(
            new ThreadCompactionContext(thread, thread.Messages, null, client),
            SummarizeAtHead());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tool request instead of a continuation handoff*");
        thread.Messages.Should().HaveCount(2);
    }

    private static CompactionSpecification RemoveAtHead(CompactionCommitMode mode) => new()
    {
        Point = new CompactAtCurrentHead(),
        Strategy = new RemovalCompaction(),
        CommitMode = mode
    };

    private static CompactionSpecification SummarizeAtHead() => new()
    {
        Point = new CompactAtCurrentHead(),
        Strategy = new SummarizingCompaction(),
        CommitMode = CompactionCommitMode.Soft
    };

    private static Thread CreateThread(int messageCount)
    {
        var thread = new Thread("session", "thread", "test-agent");
        for (var i = 0; i < messageCount; i++)
        {
            thread.Messages.Add(new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"message {i}")
            {
                MessageId = $"message-{i}",
                CreatedAt = DateTimeOffset.UtcNow,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["hpd.messageTurnId"] = $"turn-{i / 2}"
                }
            });
        }
        return thread;
    }

    private sealed class StaticSeedProvider(params AgentEvent[] events) : IThreadJournalRebaseSeedProvider
    {
        public ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
            ThreadKey thread,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentEvent>>(events);
    }

    private sealed class CapturingChatClient(ChatMessage response) : IChatClient
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            CancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(response));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class TestInputRequestContent(string requestId) : InputRequestContent(requestId);
    private sealed class TestInputResponseContent(string requestId) : InputResponseContent(requestId);
}
