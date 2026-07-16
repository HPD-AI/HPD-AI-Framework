using System.Text.Json;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;
using HPD.Events;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Session;

public class ThreadEventStoreTests : AgentTestBase
{
    private static string EventType(AgentEvent evt) => AgentEventSerializer.GetEventTypeName(evt);

    [Fact]
    public async Task InMemoryStore_ProjectsThreadFromEvents()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");

        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync(session.Id, thread);

        var userMessage = new ChatMessage(ChatRole.User, "hello");
        userMessage.MessageId = "msg-1";
        await store.AppendThreadEventAsync(session.Id, thread.Id, ThreadEventFactory.TextMessageStarted(session.Id, thread.Id, null, "msg-1", ChatRole.User.Value, 0));
        await store.AppendThreadEventAsync(session.Id, thread.Id, ThreadEventFactory.TextDelta(session.Id, thread.Id, null, "msg-1", "hello", 0));
        await store.AppendThreadEventAsync(session.Id, thread.Id, ThreadEventFactory.TextMessageCompleted(session.Id, thread.Id, null, "msg-1", 0));

        var loaded = await store.ProjectThreadAsync(session.Id, thread.Id, ThreadProjectionPurpose.ThreadHistory);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Messages);
        Assert.Equal("msg-1", loaded.Messages[0].MessageId);
        Assert.Equal("hello", loaded.Messages[0].Text);
    }

    [Fact]
    public async Task SaveInitialThread_RootThread_DoesNotWriteRedundantStateEvents()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");
        thread.Name = "main";

        await store.SaveInitialThreadAsync(session.Id, thread);

        var events = await store.CollectThreadEventsAsync(session.Id, thread.Id);
        var evt = Assert.Single(events!);
        Assert.IsType<ThreadCreatedEvent>(evt);
        Assert.Equal(1, evt.ThreadSequenceNumber);

        var projected = await store.ProjectThreadAsync(session.Id, thread.Id, ThreadProjectionPurpose.ThreadHistory);
        Assert.Equal("main", projected!.Name);
        Assert.Null(projected.ForkedFrom);
        Assert.Null(projected.ForkedAtMessageId);
        Assert.Null(projected.ForkedAtMessageIndex);
        Assert.Empty(projected.ChildThreads);
    }

    [Fact]
    public void ThreadProjector_ProjectsTranscriptEvents_AndIgnoresTurnRuntimeEvents()
    {
        var thread = new Thread("session-1", "main");
        var message = new ChatMessage(ChatRole.User, "durable");
        message.MessageId = "msg-1";

        var events = Sequence(
            [
                ThreadEventFactory.ThreadCreated(thread),
                ThreadEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 1, false),
                ThreadEventFactory.TextMessageStarted("session-1", "main", null, "msg-1", ChatRole.User.Value, 0),
                ThreadEventFactory.TextDelta("session-1", "main", null, "msg-1", "durable", 0),
                ThreadEventFactory.TextMessageCompleted("session-1", "main", null, "msg-1", 0),
                ThreadEventFactory.TurnCompleted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 1, "done", TimeSpan.FromMilliseconds(10), 1)
            ]);

        var projected = ThreadProjector.Project("session-1", "main", events, ThreadProjectionPurpose.ThreadHistory);

        Assert.Single(projected.Messages);
        Assert.Equal("durable", projected.Messages[0].Text);
    }

    [Fact]
    public void ThreadProjector_CoalescesStreamingDeltasIntoDurableMessageContents()
    {
        var thread = new Thread("session-1", "main");
        var message = new ChatMessage(ChatRole.Assistant, []) { MessageId = "assistant-1" };
        message.AdditionalProperties ??= [];
        message.AdditionalProperties["quote"] = new Dictionary<string, object?>
        {
            ["text"] = "quoted context",
            ["messageId"] = "user-1",
            ["source"] = "selection"
        };

        var events = Sequence(
            [
                ThreadEventFactory.ThreadCreated(thread),
                ThreadEventFactory.ReasoningStarted("session-1", "main", "turn-1", "assistant-1", ChatRole.Assistant.Value, 0),
                ThreadEventFactory.ReasoningDelta("session-1", "main", "turn-1", "assistant-1", "first ", null, 0),
                ThreadEventFactory.ReasoningDelta("session-1", "main", "turn-1", "assistant-1", "thought", null, 0),
                ThreadEventFactory.ReasoningCompleted("session-1", "main", "turn-1", "assistant-1", 0),
                ThreadEventFactory.TextMessageStarted(
                    "session-1",
                    "main",
                    "turn-1",
                    "assistant-1",
                    ChatRole.Assistant.Value,
                    AgentMessageSource.AssistantOutput,
                    AgentMessageVisibility.Transcript,
                    0,
                    additionalProperties: message.AdditionalProperties),
                ThreadEventFactory.TextDelta("session-1", "main", "turn-1", "assistant-1", "final ", 0),
                ThreadEventFactory.TextDelta("session-1", "main", "turn-1", "assistant-1", "answer", 0),
                ThreadEventFactory.TextMessageCompleted("session-1", "main", "turn-1", "assistant-1", 0)
            ]);

        var projected = ThreadProjector.Project("session-1", "main", events, ThreadProjectionPurpose.ThreadHistory);

        var projectedMessage = Assert.Single(projected.Messages);
        var reasoning = Assert.Single(projectedMessage.Contents.OfType<TextReasoningContent>());
        var text = Assert.Single(projectedMessage.Contents.OfType<TextContent>());
        Assert.Equal("first thought", reasoning.Text);
        Assert.Equal("final answer", text.Text);
        Assert.True(projectedMessage.AdditionalProperties?.ContainsKey("quote"));
        Assert.Equal("turn-1", projectedMessage.AdditionalProperties?[
            "hpd.messageTurnId"]);
    }

    [Fact]
    public void ThreadProjector_ProjectsSelfContainedToolCallEndIntoMeaiHistory()
    {
        var thread = new Thread("session-1", "main");
        var assistant = new ChatMessage(ChatRole.Assistant, []) { MessageId = "assistant-1" };
        var tool = new ChatMessage(ChatRole.Tool, []) { MessageId = "tool-1" };

        var events = Sequence(
            [
                ThreadEventFactory.ThreadCreated(thread),
                ThreadEventFactory.ToolCallStarted(
                    "session-1",
                    "main",
                    "turn-1",
                    "call-1",
                    "ListDirectory",
                    "assistant-1",
                    null,
                    null,
                    0),
                ThreadEventFactory.ToolCallArgs("session-1", "main", "turn-1", "call-1", """{"path":"/workspace"}""", 0),
                ThreadEventFactory.ToolCallCompleted(
                    "session-1",
                    "main",
                    "turn-1",
                    "call-1",
                    0,
                    "assistant-1",
                    "ListDirectory",
                    """{"path":"/workspace"}"""),
                ThreadEventFactory.ToolCallResult(
                    "session-1",
                    "main",
                    "turn-1",
                    "call-1",
                    "tool-1",
                    new ToolResultPayload(Text: "workspace files"),
                    null,
                    null,
                    0,
                    "ListDirectory")
            ]);

        var projected = ThreadProjector.Project("session-1", "main", events, ThreadProjectionPurpose.ThreadHistory);

        var assistantMessage = Assert.Single(projected.Messages.Where(m => m.Role == ChatRole.Assistant));
        var call = Assert.Single(assistantMessage.Contents.OfType<FunctionCallContent>());
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("ListDirectory", call.Name);
        AssertArgument(call.Arguments, "path", "/workspace");

        var toolMessage = Assert.Single(projected.Messages.Where(m => m.Role == ChatRole.Tool));
        var result = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        Assert.Equal("call-1", result.CallId);
        AssertNoOrphanFunctionResults(projected.Messages);
    }

    [Fact]
    public void ThreadRunProjector_ProjectsPersistedOpenRunAsInterrupted_WhenRuntimeIsNotLive()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var runs = ThreadRunProjector.Project(
            "agent-1",
            "session-1",
            "main",
            [
                new ThreadRunStartedEvent("run-1", "agent-1", startedAt)
            ]);

        var run = Assert.Single(runs);
        Assert.Equal("run-1", run.RuntimeRunId);
        Assert.Equal(ThreadRunStatus.Interrupted, run.Status);
        Assert.Null(run.CompletedAt);
    }

    [Fact]
    public void ThreadRunProjector_ProjectsPersistedOpenRunAsActive_WhenRuntimeIsLive()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var runs = ThreadRunProjector.Project(
            "agent-1",
            "session-1",
            "main",
            [
                new ThreadRunStartedEvent("run-1", "agent-1", startedAt)
            ],
            activeRuntimeRunId: "run-1");

        var run = Assert.Single(runs);
        Assert.Equal("run-1", run.RuntimeRunId);
        Assert.Equal(ThreadRunStatus.Active, run.Status);
    }

    [Fact]
    public async Task InMemoryStore_ReadThreadEvents_UsesBoundedSequenceRange()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");

        await store.SaveInitialThreadAsync(session.Id, thread);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 0, false));

        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            new ThreadKey(session.Id, thread.Id),
            new ThreadEventReadRequest(ThreadJournalCursor.Start(1), MaxBatchEventCount: 2)))
        {
            events.AddRange(batch.Events);
            break;
        }

        Assert.Equal(2, events.Count);
        Assert.Equal([1, 2], events.Select(e => e.ThreadSequenceNumber));
    }

    private static IReadOnlyList<AgentEvent> Sequence(IReadOnlyList<AgentEvent> events) =>
        events.Select((evt, index) => evt with { ThreadSequenceNumber = index + 1 }).ToArray();

    [Fact]
    public async Task FileSessionStore_WritesSegmentedCanonicalJournalAndDescriptor()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var store = new FileSessionStore(tempDir);
            var session = new HPD.Agent.Session("session-1");
            var thread = session.CreateThread("main");

            await store.SaveSessionAsync(session);
            await store.SaveInitialThreadAsync(session.Id, thread);
            await store.AppendThreadEventAsync(
                session.Id,
                thread.Id,
                ThreadEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 0, false));

            var threadDir = Path.Combine(tempDir, "sessions", "session-1", "threads", "main");
            var journalDir = Path.Combine(threadDir, "journal");
            var descriptorPath = Path.Combine(threadDir, "thread.descriptor.json");
            var indexPath = Path.Combine(threadDir, "journal.index");

            var segmentPath = Assert.Single(Directory.GetFiles(journalDir, "segment-*.events"));
            Assert.True(File.Exists(descriptorPath));
            Assert.True(File.Exists(indexPath));

            var eventDocuments = (await File.ReadAllLinesAsync(segmentPath))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line))
                .ToList();
            var events = eventDocuments
                .SelectMany(document => document.RootElement.EnumerateArray().Select(element => element.Clone()))
                .ToList();

            Assert.Contains(events, e => e.GetProperty("type").GetString() == ThreadEventTypes.ThreadCreated);
            Assert.Contains(events, e => e.GetProperty("type").GetString() == EventTypes.MessageTurn.MESSAGE_TURN_STARTED);
            Assert.All(events, e =>
            {
                Assert.Equal(session.Id, e.GetProperty("sessionId").GetString());
                Assert.Equal(thread.Id, e.GetProperty("threadId").GetString());
                Assert.True(e.GetProperty("threadSequenceNumber").GetInt64() > 0);
            });

            using var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(descriptorPath));
            Assert.Equal("hpd.agent.thread-descriptor", descriptor.RootElement.GetProperty("schema").GetString());
            Assert.Equal(2, descriptor.RootElement.GetProperty("descriptor").GetProperty("head").GetInt64());
            Assert.Equal("MainAgent", descriptor.RootElement.GetProperty("descriptor").GetProperty("kind").GetString());
            Assert.Equal("Visible", descriptor.RootElement.GetProperty("descriptor").GetProperty("visibility").GetString());

            var document = await store.CollectThreadEventsAsync(session.Id, thread.Id, TestCancellationToken);
            Assert.NotNull(document);
            Assert.Contains(document, e => EventType(e) == ThreadEventTypes.ThreadCreated);
            Assert.Contains(document, e => EventType(e) == EventTypes.MessageTurn.MESSAGE_TURN_STARTED);
            Assert.All(document, e =>
            {
                Assert.Equal(session.Id, e.SessionId);
                Assert.Equal(thread.Id, e.ThreadId);
            });

            var loadedThread = await store.ProjectThreadAsync(session.Id, thread.Id, ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
            Assert.NotNull(loadedThread);

            foreach (var eventDocument in eventDocuments)
                eventDocument.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FileSessionStore_Projection_PreservesToolCallAcrossSeparateAppendFrames()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-tool-cache-{Guid.NewGuid():N}");

        try
        {
            var store = new FileSessionStore(tempDir);
            var session = new HPD.Agent.Session("session-1");
            var thread = session.CreateThread("main");
            var assistant = new ChatMessage(ChatRole.Assistant, []) { MessageId = "assistant-1" };
            var tool = new ChatMessage(ChatRole.Tool, []) { MessageId = "tool-1" };

            await store.SaveSessionAsync(session);
            await store.SaveInitialThreadAsync(session.Id, thread);
            await store.AppendThreadEventAsync(
                session.Id,
                thread.Id,
                ThreadEventFactory.ToolCallStarted(
                    session.Id,
                    thread.Id,
                    "turn-1",
                    "call-1",
                    "ReadFile",
                    "assistant-1",
                    null,
                    null,
                    0));
            await store.AppendThreadEventAsync(
                session.Id,
                thread.Id,
                ThreadEventFactory.ToolCallArgs(session.Id, thread.Id, "turn-1", "call-1", """{"path":"README.md"}""", 0));

            var cachedBeforeEnd = await store.ProjectThreadAsync(session.Id, thread.Id, ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
            Assert.NotNull(cachedBeforeEnd);
            Assert.DoesNotContain(
                cachedBeforeEnd.Messages.SelectMany(message => message.Contents),
                content => content is FunctionCallContent);

            await store.AppendThreadEventAsync(
                session.Id,
                thread.Id,
                ThreadEventFactory.ToolCallCompleted(
                    session.Id,
                    thread.Id,
                    "turn-1",
                    "call-1",
                    0,
                    "assistant-1",
                    "ReadFile",
                    """{"path":"README.md"}"""));
            await store.AppendThreadEventAsync(
                session.Id,
                thread.Id,
                ThreadEventFactory.ToolCallResult(
                    session.Id,
                    thread.Id,
                    "turn-1",
                    "call-1",
                    "tool-1",
                    new ToolResultPayload(Text: "contents"),
                    null,
                    null,
                    0,
                    "ReadFile"));

            var loaded = await store.ProjectThreadAsync(session.Id, thread.Id, ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);

            Assert.NotNull(loaded);
            var assistantMessage = Assert.Single(loaded.Messages.Where(m => m.Role == ChatRole.Assistant));
            var call = Assert.Single(assistantMessage.Contents.OfType<FunctionCallContent>());
            Assert.Equal("call-1", call.CallId);
            Assert.Equal("ReadFile", call.Name);
            AssertArgument(call.Arguments, "path", "README.md");

            var toolMessage = Assert.Single(loaded.Messages.Where(m => m.Role == ChatRole.Tool));
            var result = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
            Assert.Equal("call-1", result.CallId);
            AssertNoOrphanFunctionResults(loaded.Messages);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task InMemoryStore_AppendThreadEvent_AssignsMissingDurableIdentityAndScope()
    {
        var store = new InMemorySessionStore();

        await store.AppendThreadEventAsync("session-1", "main", new TextDeltaEvent("hello", "msg-1"));

        var document = await store.CollectThreadEventsAsync("session-1", "main");
        var evt = Assert.Single(document!);
        Assert.False(string.IsNullOrWhiteSpace(evt.EventId));
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal("main", evt.ThreadId);
    }

    [Fact]
    public async Task FileSessionStore_AppendThreadEvent_AssignsMissingDurableIdentityAndScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var store = new FileSessionStore(tempDir);

            await store.AppendThreadEventAsync("session-1", "main", new TextDeltaEvent("hello", "msg-1"));

            var document = await store.CollectThreadEventsAsync("session-1", "main");
            var evt = Assert.Single(document!);
            Assert.False(string.IsNullOrWhiteSpace(evt.EventId));
            Assert.Equal("session-1", evt.SessionId);
            Assert.Equal("main", evt.ThreadId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppendThreadEvent_ReturnsCommittedValueWithoutMutatingProposedEvent(bool useFileStore)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            ISessionStore store = useFileStore
                ? new FileSessionStore(tempDir)
                : new InMemorySessionStore();
            var supplied = ThreadEventFactory.TextDelta(
                "session-1", "main", "turn-1", "msg-1", "hello", 0);

            var committed = await store.AppendThreadEventAsync("session-1", "main", supplied);

            var persisted = Assert.Single((await store.CollectThreadEventsAsync("session-1", "main"))!);
            Assert.Equal(persisted.EventId, supplied.EventId);
            Assert.Equal(persisted.ThreadSequenceNumber, committed.ThreadSequenceNumber);
            Assert.True(committed.ThreadSequenceNumber > 0);
            Assert.Equal(0, supplied.ThreadSequenceNumber);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FileSessionStore_AppendThreadEvent_PersistsCanonicalScopeAndFlowId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var store = new FileSessionStore(tempDir);

            await store.AppendThreadEventAsync(
                "session-1",
                "main",
                ThreadEventFactory.TextDelta("session-1", "main", "turn-1", "msg-1", "hello", 0));

            var document = await store.CollectThreadEventsAsync("session-1", "main");
            var evt = Assert.Single(document!);
            Assert.Equal("turn-1", evt.EventFlowId);
            Assert.Equal("session-1", evt.SessionId);
            Assert.Equal("main", evt.ThreadId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppendThreadEvent_RejectsConflictingScope(bool useFileStore)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            ISessionStore store = useFileStore
                ? new FileSessionStore(tempDir)
                : new InMemorySessionStore();
            var evt = new TextDeltaEvent("hello", "msg-1")
            {
                SessionId = "different-session",
                ThreadId = "main"
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.AppendThreadEventAsync("session-1", "main", evt).AsTask());

            Assert.Contains("does not match", exception.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Agent_PersistsInputAndRuntimeOutputWithoutDuplicateTranscriptEvents()
    {
        var store = new InMemorySessionStore();
        var config = DefaultConfig();
        config.SessionStore = store;
        var client = new FakeChatClient();
        client.EnqueueTextResponse("hello human");
        var agent = CreateAgent(config, client);
        var sessionId = "session-1";

        await agent.CreateSessionAsync(sessionId, cancellationToken: TestCancellationToken);
        await agent.RunAsync("who are you", sessionId, "main", cancellationToken: TestCancellationToken);

        var document = await store.CollectThreadEventsAsync(sessionId, "main", TestCancellationToken);
        Assert.NotNull(document);

        var textDeltas = document.OfType<TextDeltaEvent>().ToList();
        Assert.Contains(textDeltas, e => e.Text == "who are you");
        Assert.Contains(textDeltas, e => e.Text == "hello human");

        var loaded = await store.ProjectThreadAsync(sessionId, "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("who are you", loaded.Messages[0].Text);
        Assert.Equal("hello human", loaded.Messages[1].Text);
    }

    [Fact]
    public async Task Agent_PersistsTurnFailed_WhenSessionTurnFaults()
    {
        var store = new InMemorySessionStore();
        var config = DefaultConfig();
        config.SessionStore = store;
        var client = new FakeChatClient();
        var agent = CreateAgent(config, client);
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");

        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync(session.Id, thread);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in agent.RunTurnStreamAsync(
                [new ChatMessage(ChatRole.User, "fail please")],
                session,
                thread,
                cancellationToken: TestCancellationToken))
            {
            }
        });

        Assert.Contains("No responses queued", ex.Message);

        var document = await store.CollectThreadEventsAsync(session.Id, thread.Id, TestCancellationToken);
        var failed = Assert.IsType<MessageTurnErrorEvent>(Assert.Single(document!.Where(e => EventType(e) == EventTypes.MessageTurn.MESSAGE_TURN_ERROR)));

        Assert.False(string.IsNullOrWhiteSpace(failed.MessageTurnId));
        Assert.Equal("session-1", failed.ConversationId);
        Assert.Equal(nameof(InvalidOperationException), failed.ErrorType);
        Assert.Contains("No responses queued", failed.ErrorMessage);
    }

    [Fact]
    public async Task Agent_PersistsReasoningEvents_WhenReasoningExcludedFromModelHistory()
    {
        var store = new InMemorySessionStore();
        var config = DefaultConfig();
        config.SessionStore = store;
        config.IncludeReasoningInModelHistory = false;
        var client = new FakeChatClient();
        client.EnqueueReasoningThenTextResponse("private thought", "public answer", "protected-reasoning");
        client.EnqueueTextResponse("second answer");
        var agent = CreateAgent(config, client);
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");

        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync(session.Id, thread);

        await foreach (var _ in agent.RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, "think then answer")],
            session,
            thread,
            cancellationToken: TestCancellationToken))
        {
        }

        var document = await store.CollectThreadEventsAsync(session.Id, thread.Id, TestCancellationToken);
        Assert.NotNull(document);

        var reasoningEvents = document
            .Where(e => EventType(e) is EventTypes.Reasoning.REASONING_MESSAGE_START
                or EventTypes.Reasoning.REASONING_DELTA
                or EventTypes.Reasoning.REASONING_MESSAGE_END)
            .ToList();

        Assert.Contains(reasoningEvents, e => EventType(e) == EventTypes.Reasoning.REASONING_MESSAGE_START);
        Assert.Contains(reasoningEvents, e => EventType(e) == EventTypes.Reasoning.REASONING_MESSAGE_END);

        var matchingDeltas = document
            .OfType<ReasoningDeltaEvent>()
            .Where(e => e.Text == "private thought" && e.ProtectedData == "protected-reasoning")
            .ToList();
        Assert.NotEmpty(matchingDeltas);

        var loaded = await store.ProjectThreadAsync(session.Id, thread.Id, ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        Assert.NotNull(loaded);
        var assistantMessage = Assert.Single(loaded.Messages.Where(m => m.Role == ChatRole.Assistant));
        var committedReasoning = Assert.Single(assistantMessage.Contents.OfType<TextReasoningContent>());
        Assert.Equal("private thought", committedReasoning.Text);
        Assert.Equal("protected-reasoning", committedReasoning.ProtectedData);
        Assert.Equal("public answer", assistantMessage.Text);

        await foreach (var _ in agent.RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, "second turn")],
            session,
            thread,
            cancellationToken: TestCancellationToken))
        {
        }

        Assert.Equal(2, client.CapturedRequests.Count);
        Assert.DoesNotContain(
            client.CapturedRequests[1].SelectMany(m => m.Contents),
            c => c is TextReasoningContent);
    }

    [Fact]
    public async Task Agent_PersistsToolCallEvents_WhenToolEventsAreStreamed()
    {
        var store = new InMemorySessionStore();
        var config = DefaultConfig();
        config.SessionStore = store;
        var client = new FakeChatClient();
        client.EnqueueToolCall(
            functionName: "Calculator",
            callId: "call-1",
            args: new Dictionary<string, object?> { ["expression"] = "2+2" });
        client.EnqueueTextResponse("The answer is 4");
        var calculator = AIFunctionFactory.Create(
            (string expression) => expression == "2+2" ? "4" : "unknown",
            name: "Calculator",
            description: "Evaluates simple expressions.");
        var agent = CreateAgent(config, client, tools: [calculator]);
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("main");

        await store.SaveSessionAsync(session);
        await store.SaveInitialThreadAsync(session.Id, thread);

        await foreach (var _ in agent.RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, "what is 2+2?")],
            session,
            thread,
            cancellationToken: TestCancellationToken))
        {
        }

        var document = await store.CollectThreadEventsAsync(session.Id, thread.Id, TestCancellationToken);
        Assert.NotNull(document);

        var toolEventTypes = document
            .Where(e => EventType(e) is EventTypes.Tool.TOOL_CALL_START
                or EventTypes.Tool.TOOL_CALL_ARGS
                or EventTypes.Tool.TOOL_CALL_RESULT
                or EventTypes.Tool.TOOL_CALL_END)
            .Select(EventType)
            .ToArray();

        Assert.Contains(EventTypes.Tool.TOOL_CALL_START, toolEventTypes);
        Assert.Contains(EventTypes.Tool.TOOL_CALL_ARGS, toolEventTypes);
        Assert.Contains(EventTypes.Tool.TOOL_CALL_END, toolEventTypes);
        Assert.Contains(EventTypes.Tool.TOOL_CALL_RESULT, toolEventTypes);

        var started = Assert.IsType<ToolCallStartEvent>(
            document.Last(e => EventType(e) == EventTypes.Tool.TOOL_CALL_START));
        Assert.Equal("call-1", started.CallId);
        Assert.Equal("Calculator", started.Name);

        var completed = Assert.IsType<ToolCallEndEvent>(
            document.Last(e => EventType(e) == EventTypes.Tool.TOOL_CALL_END));
        Assert.Equal("call-1", completed.CallId);
        Assert.NotNull(completed.MessageId);
        Assert.Equal("Calculator", completed.Name);
        using var completedArgs = JsonDocument.Parse(completed.ArgsJson!);
        Assert.Equal("2+2", completedArgs.RootElement.GetProperty("expression").GetString());
    }

    private static void AssertArgument(IDictionary<string, object?>? arguments, string name, string expected)
    {
        Assert.NotNull(arguments);
        Assert.True(arguments!.TryGetValue(name, out var value), $"Expected argument '{name}'.");

        var actual = value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => value?.ToString()
        };

        Assert.Equal(expected, actual);
    }

    private static void AssertNoOrphanFunctionResults(IEnumerable<ChatMessage> messages)
    {
        var seenCalls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var content in messages.SelectMany(message => message.Contents))
        {
            if (content is FunctionCallContent call)
                seenCalls.Add(call.CallId);
            else if (content is FunctionResultContent result)
                Assert.Contains(result.CallId, seenCalls);
        }
    }
}
