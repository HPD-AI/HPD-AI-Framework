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

        var loaded = await store.LoadThreadAsync(session.Id, thread.Id);

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

        var document = await store.LoadThreadDocumentAsync(session.Id, thread.Id);
        var evt = Assert.Single(document!.Events);
        Assert.IsType<ThreadCreatedEvent>(evt);
        Assert.Equal(2, document.NextSequenceNumber);

        var projected = await store.LoadThreadAsync(session.Id, thread.Id);
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

        var document = ThreadEventDocumentBuilder.Create(
            "session-1",
            "main",
            [
                ThreadEventFactory.ThreadCreated(thread),
                ThreadEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 1, false),
                ThreadEventFactory.TextMessageStarted("session-1", "main", null, "msg-1", ChatRole.User.Value, 0),
                ThreadEventFactory.TextDelta("session-1", "main", null, "msg-1", "durable", 0),
                ThreadEventFactory.TextMessageCompleted("session-1", "main", null, "msg-1", 0),
                ThreadEventFactory.TurnCompleted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 1, "done", TimeSpan.FromMilliseconds(10), 1)
            ]);

        var projected = ThreadProjector.Project(document);

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

        var document = ThreadEventDocumentBuilder.Create(
            "session-1",
            "main",
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

        var projected = ThreadProjector.Project(document);

        var projectedMessage = Assert.Single(projected.Messages);
        var reasoning = Assert.Single(projectedMessage.Contents.OfType<TextReasoningContent>());
        var text = Assert.Single(projectedMessage.Contents.OfType<TextContent>());
        Assert.Equal("first thought", reasoning.Text);
        Assert.Equal("final answer", text.Text);
        Assert.True(projectedMessage.AdditionalProperties?.ContainsKey("quote"));
        Assert.Equal("turn-1", projectedMessage.AdditionalProperties?[
            ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName]);
    }

    [Fact]
    public void ThreadProjector_ProjectsSelfContainedToolCallEndIntoMeaiHistory()
    {
        var thread = new Thread("session-1", "main");
        var assistant = new ChatMessage(ChatRole.Assistant, []) { MessageId = "assistant-1" };
        var tool = new ChatMessage(ChatRole.Tool, []) { MessageId = "tool-1" };

        var document = ThreadEventDocumentBuilder.Create(
            "session-1",
            "main",
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

        var projected = ThreadProjector.Project(document);

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
    public void ThreadEventFactory_PersistsRequestSessionProjectionEvents()
    {
        var now = DateTimeOffset.UtcNow;
        AgentEvent[] events =
        [
            new AgentRequestStartedEvent(
                "request-1",
                "source",
                "PermissionRequestEvent",
                "PermissionResponseEvent",
                ResponsePolicy.FirstValidResponseWins,
                null,
                RequestVisibility.AllObservers,
                now),
            new AgentRequestResolvedEvent(
                "request-1",
                "source",
                "PermissionRequestEvent",
                "PermissionResponseEvent",
                null,
                null,
                now),
            new AgentRequestExpiredEvent(
                "request-2",
                "source",
                "PermissionRequestEvent",
                TimeSpan.FromSeconds(30),
                now),
            new AgentRequestCancelledEvent(
                "request-3",
                "source",
                "PermissionRequestEvent",
                "cancelled",
                now),
            new AgentResponseRejectedEvent(
                "request-1",
                "PermissionResponseEvent",
                RespondStatus.AlreadyResolved,
                "Request has already resolved.",
                "client-a",
                null,
                now)
        ];

        foreach (var evt in events)
        {
            var threadEvent = ThreadEventFactory.FromAgentEvent(
                "session-1",
                "main",
                evt,
                messageTurnId: null,
                conversationId: null,
                iteration: 0,
                inputMessageCount: 0,
                isResume: false,
                terminationReason: null,
                turnMessageCount: 0);

            Assert.NotNull(threadEvent);
            Assert.True(evt.ShouldPersistToThread());
            Assert.Equal("session-1", threadEvent.SessionId);
            Assert.Equal("main", threadEvent.ThreadId);
        }
    }

    [Fact]
    public async Task InMemoryStore_ReadThreadEvents_UsesReplayOptions()
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
        await foreach (var evt in store.ReadThreadEventsAsync(
            session.Id,
            thread.Id,
            new HPD.Events.ReplayReadOptions(null, null, null, 2)))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal([1, 2], events.Select(e => e.SequenceNumber));
    }

    [Fact]
    public async Task JsonSessionStore_WritesThreadEventStreamAndLazyProjectionCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = new HPD.Agent.Session("session-1");
            var thread = session.CreateThread("main");

            await store.SaveSessionAsync(session);
            await store.SaveInitialThreadAsync(session.Id, thread);
            await store.AppendThreadEventAsync(
                session.Id,
                thread.Id,
                ThreadEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 0, false));

            var threadDir = Path.Combine(tempDir, "session-1", "threads", "main");
            var threadJsonPath = Path.Combine(threadDir, "thread.json");
            var eventsPath = Path.Combine(threadDir, "thread.events.jsonl");
            var metadataPath = Path.Combine(threadDir, "thread.meta.json");
            var oldSnapshotPath = Path.Combine(threadDir, "thread.snapshot.json");
            var projectionPath = Path.Combine(threadDir, "thread.projection.json");

            Assert.False(File.Exists(threadJsonPath));
            Assert.False(File.Exists(oldSnapshotPath));
            Assert.True(File.Exists(eventsPath));
            Assert.True(File.Exists(metadataPath));
            Assert.False(File.Exists(projectionPath));

            var eventDocuments = (await File.ReadAllLinesAsync(eventsPath))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line))
                .ToList();
            var events = eventDocuments.Select(document => document.RootElement).ToList();

            Assert.Contains(events, e => e.GetProperty("type").GetString() == ThreadEventTypes.ThreadCreated);
            Assert.Contains(events, e => e.GetProperty("type").GetString() == EventTypes.MessageTurn.MESSAGE_TURN_STARTED);
            Assert.All(events, e =>
            {
                Assert.False(e.TryGetProperty("sessionId", out _));
                Assert.False(e.TryGetProperty("threadId", out _));
                Assert.False(e.TryGetProperty("channel", out _));
                Assert.False(e.TryGetProperty("kind", out _));
                Assert.False(e.TryGetProperty("direction", out _));
                Assert.False(e.TryGetProperty("canInterrupt", out _));
                Assert.False(e.TryGetProperty("exchangeTimestampNs", out _));
            });

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
            Assert.Equal("hpd.agent.thread.meta", metadata.RootElement.GetProperty("schema").GetString());
            Assert.Equal(3, metadata.RootElement.GetProperty("nextSequenceNumber").GetInt64());
            Assert.Equal("MainAgent", metadata.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Visible", metadata.RootElement.GetProperty("visibility").GetString());

            var document = await store.LoadThreadDocumentAsync(session.Id, thread.Id, TestCancellationToken);
            Assert.NotNull(document);
            Assert.Contains(document.Events, e => EventType(e) == ThreadEventTypes.ThreadCreated);
            Assert.Contains(document.Events, e => EventType(e) == EventTypes.MessageTurn.MESSAGE_TURN_STARTED);
            Assert.All(document.Events, e =>
            {
                Assert.Equal(session.Id, e.SessionId);
                Assert.Equal(thread.Id, e.ThreadId);
            });

            Assert.False(File.Exists(projectionPath));

            var loadedThread = await store.LoadThreadAsync(session.Id, thread.Id, TestCancellationToken);
            Assert.NotNull(loadedThread);
            Assert.True(File.Exists(projectionPath));

            using var projection = JsonDocument.Parse(await File.ReadAllTextAsync(projectionPath));
            Assert.Equal("hpd.agent.thread.projection-cache", projection.RootElement.GetProperty("schema").GetString());
            Assert.Equal(ThreadProjectionCache.CurrentVersion, projection.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(2, projection.RootElement.GetProperty("lastSequenceNumber").GetInt64());

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
    public async Task JsonSessionStore_IncrementalProjection_PreservesToolCallWhenCacheSplitsToolEvents()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-tool-cache-{Guid.NewGuid():N}");

        try
        {
            var store = new JsonSessionStore(tempDir);
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

            var cachedBeforeEnd = await store.LoadThreadAsync(session.Id, thread.Id, TestCancellationToken);
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

            var loaded = await store.LoadThreadAsync(session.Id, thread.Id, TestCancellationToken);

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

        var document = await store.LoadThreadDocumentAsync("session-1", "main");
        var evt = Assert.Single(document!.Events);
        Assert.False(string.IsNullOrWhiteSpace(evt.EventId));
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal("main", evt.ThreadId);
    }

    [Fact]
    public async Task JsonSessionStore_AppendThreadEvent_AssignsMissingDurableIdentityAndScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var store = new JsonSessionStore(tempDir);

            await store.AppendThreadEventAsync("session-1", "main", new TextDeltaEvent("hello", "msg-1"));

            var document = await store.LoadThreadDocumentAsync("session-1", "main");
            var evt = Assert.Single(document!.Events);
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

    [Fact]
    public async Task JsonSessionStore_AppendThreadEvent_PersistsEventFlowId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var store = new JsonSessionStore(tempDir);

            await store.AppendThreadEventAsync(
                "session-1",
                "main",
                ThreadEventFactory.TextDelta("session-1", "main", "turn-1", "msg-1", "hello", 0));

            var eventsPath = Path.Combine(tempDir, "session-1", "threads", "main", "thread.events.jsonl");
            var line = Assert.Single(await File.ReadAllLinesAsync(eventsPath));
            using var json = JsonDocument.Parse(line);

            Assert.Equal("turn-1", json.RootElement.GetProperty("eventFlowId").GetString());

            var document = await store.LoadThreadDocumentAsync("session-1", "main");
            var evt = Assert.Single(document!.Events);
            Assert.Equal("turn-1", evt.EventFlowId);
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
    public async Task AppendThreadEvent_RejectsConflictingScope(bool useJsonStore)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            ISessionStore store = useJsonStore
                ? new JsonSessionStore(tempDir)
                : new InMemorySessionStore();
            var evt = new TextDeltaEvent("hello", "msg-1")
            {
                SessionId = "different-session",
                ThreadId = "main"
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.AppendThreadEventAsync("session-1", "main", evt));

            Assert.Contains("does not match", exception.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonSessionStore_LoadThreadDocument_HydratesEventScopeFromStreamMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var threadDir = Path.Combine(tempDir, "session-1", "threads", "main");
            Directory.CreateDirectory(threadDir);
            await File.WriteAllTextAsync(
                Path.Combine(threadDir, "thread.meta.json"),
                """
                {
                  "schema": "hpd.agent.thread.event-stream",
                  "version": 1,
                  "sessionId": "session-1",
                  "threadId": "main",
                  "createdAt": "2026-01-01T00:00:00+00:00",
                  "updatedAt": "2026-01-01T00:00:00+00:00",
                  "nextSequenceNumber": 2
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(threadDir, "thread.events.jsonl"),
                """
                {"type":"THREAD_CREATED","eventId":"evt-1","name":"Main","createdAt":"2026-01-01T00:00:00+00:00","sequenceNumber":1}
                
                """);

            var store = new JsonSessionStore(tempDir);

            var document = await store.LoadThreadDocumentAsync("session-1", "main");

            var evt = Assert.Single(document!.Events);
            Assert.Equal("session-1", evt.SessionId);
            Assert.Equal("main", evt.ThreadId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonSessionStore_LoadThreadDocument_RejectsConflictingEventScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-thread-events-{Guid.NewGuid():N}");

        try
        {
            var threadDir = Path.Combine(tempDir, "session-1", "threads", "main");
            Directory.CreateDirectory(threadDir);
            await File.WriteAllTextAsync(
                Path.Combine(threadDir, "thread.meta.json"),
                """
                {
                  "schema": "hpd.agent.thread.event-stream",
                  "version": 1,
                  "sessionId": "session-1",
                  "threadId": "main",
                  "createdAt": "2026-01-01T00:00:00+00:00",
                  "updatedAt": "2026-01-01T00:00:00+00:00",
                  "nextSequenceNumber": 2
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(threadDir, "thread.events.jsonl"),
                """
                {"type":"THREAD_CREATED","eventId":"evt-1","sessionId":"other-session","threadId":"main","name":"Main","createdAt":"2026-01-01T00:00:00+00:00","sequenceNumber":1}
                
                """);

            var store = new JsonSessionStore(tempDir);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.LoadThreadDocumentAsync("session-1", "main"));

            Assert.Contains("session scope", exception.Message);
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

        var document = await store.LoadThreadDocumentAsync(sessionId, "main", TestCancellationToken);
        Assert.NotNull(document);

        var textDeltas = document.Events.OfType<TextDeltaEvent>().ToList();
        Assert.Contains(textDeltas, e => e.Text == "who are you");
        Assert.Contains(textDeltas, e => e.Text == "hello human");

        var loaded = await store.LoadThreadAsync(sessionId, "main", TestCancellationToken);
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

        var document = await store.LoadThreadDocumentAsync(session.Id, thread.Id, TestCancellationToken);
        var failed = Assert.IsType<MessageTurnErrorEvent>(Assert.Single(document!.Events.Where(e => EventType(e) == EventTypes.MessageTurn.MESSAGE_TURN_ERROR)));

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

        var document = await store.LoadThreadDocumentAsync(session.Id, thread.Id, TestCancellationToken);
        Assert.NotNull(document);

        var reasoningEvents = document.Events
            .Where(e => EventType(e) is EventTypes.Reasoning.REASONING_MESSAGE_START
                or EventTypes.Reasoning.REASONING_DELTA
                or EventTypes.Reasoning.REASONING_MESSAGE_END)
            .ToList();

        Assert.Contains(reasoningEvents, e => EventType(e) == EventTypes.Reasoning.REASONING_MESSAGE_START);
        Assert.Contains(reasoningEvents, e => EventType(e) == EventTypes.Reasoning.REASONING_MESSAGE_END);

        var matchingDeltas = document.Events
            .OfType<ReasoningDeltaEvent>()
            .Where(e => e.Text == "private thought" && e.ProtectedData == "protected-reasoning")
            .ToList();
        Assert.NotEmpty(matchingDeltas);

        var loaded = await store.LoadThreadAsync(session.Id, thread.Id, TestCancellationToken);
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

        var document = await store.LoadThreadDocumentAsync(session.Id, thread.Id, TestCancellationToken);
        Assert.NotNull(document);

        var toolEventTypes = document.Events
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
            document.Events.Last(e => EventType(e) == EventTypes.Tool.TOOL_CALL_START));
        Assert.Equal("call-1", started.CallId);
        Assert.Equal("Calculator", started.Name);

        var completed = Assert.IsType<ToolCallEndEvent>(
            document.Events.Last(e => EventType(e) == EventTypes.Tool.TOOL_CALL_END));
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
