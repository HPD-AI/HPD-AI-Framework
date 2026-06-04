using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Session;

public class BranchEventRepositoryTests : AgentTestBase
{
    private static string EventType(AgentEvent evt) => AgentEventSerializer.GetEventTypeName(evt);

    [Fact]
    public async Task InMemoryWorkspaceRepository_ProjectsBranchFromEvents()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");

        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync(session.Id, branch);

        var userMessage = new ChatMessage(ChatRole.User, "hello");
        userMessage.MessageId = "msg-1";
        await repository.AppendBranchEventAsync(session.Id, branch.Id, BranchEventFactory.MessageStarted(session.Id, branch.Id, userMessage));
        await repository.AppendBranchEventAsync(session.Id, branch.Id, BranchEventFactory.TextMessageStarted(session.Id, branch.Id, null, "msg-1", ChatRole.User.Value, 0));
        await repository.AppendBranchEventAsync(session.Id, branch.Id, BranchEventFactory.TextDelta(session.Id, branch.Id, null, "msg-1", "hello", 0));
        await repository.AppendBranchEventAsync(session.Id, branch.Id, BranchEventFactory.TextMessageCompleted(session.Id, branch.Id, null, "msg-1", 0));
        await repository.AppendBranchEventAsync(session.Id, branch.Id, BranchEventFactory.MessageCompleted(session.Id, branch.Id, "msg-1"));

        var loaded = await repository.LoadBranchAsync(session.Id, branch.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Messages);
        Assert.Equal("msg-1", loaded.Messages[0].MessageId);
        Assert.Equal("hello", loaded.Messages[0].Text);
    }

    [Fact]
    public void BranchProjector_ProjectsTranscriptEvents_AndIgnoresTurnRuntimeEvents()
    {
        var branch = new Branch("session-1", "main");
        var message = new ChatMessage(ChatRole.User, "durable");
        message.MessageId = "msg-1";

        var document = BranchEventDocumentBuilder.Create(
            "session-1",
            "main",
            [
                BranchEventFactory.BranchCreated(branch),
                BranchEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 1, false),
                BranchEventFactory.MessageStarted("session-1", "main", message),
                BranchEventFactory.TextMessageStarted("session-1", "main", null, "msg-1", ChatRole.User.Value, 0),
                BranchEventFactory.TextDelta("session-1", "main", null, "msg-1", "durable", 0),
                BranchEventFactory.TextMessageCompleted("session-1", "main", null, "msg-1", 0),
                BranchEventFactory.MessageCompleted("session-1", "main", "msg-1"),
                BranchEventFactory.TurnCompleted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 1, "done", TimeSpan.FromMilliseconds(10), 1)
            ]);

        var projected = BranchProjector.Project(document);

        Assert.Single(projected.Messages);
        Assert.Equal("durable", projected.Messages[0].Text);
    }

    [Fact]
    public async Task InMemoryWorkspaceRepository_ReadBranchEvents_UsesReplayOptions()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");

        await repository.SaveInitialBranchAsync(session.Id, branch);
        await repository.AppendBranchEventAsync(
            session.Id,
            branch.Id,
            BranchEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 0, false));

        var events = new List<AgentEvent>();
        await foreach (var evt in repository.ReadBranchEventsAsync(
            session.Id,
            branch.Id,
            new HPD.Events.ReplayReadOptions(null, null, null, 2)))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal([1, 2], events.Select(e => e.SequenceNumber));
    }

    [Fact]
    public async Task JsonWorkspaceStore_PersistsBranchEventsAsWorkspaceEventStream()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-branch-events-{Guid.NewGuid():N}");

        try
        {
            var repository = CreateJsonWorkspaceSessionRepository(tempDir);
            var session = new HPD.Agent.Session("session-1");
            var branch = session.CreateBranch("main");

            await repository.SaveSessionAsync(session);
            await repository.SaveInitialBranchAsync(session.Id, branch);
            await repository.AppendBranchEventAsync(
                session.Id,
                branch.Id,
                BranchEventFactory.TurnStarted("session-1", "main", "turn-1", "session-1", "agent-1", "Agent", 0, false));

            Assert.True(File.Exists(Path.Combine(tempDir, "workspace.json")));

            var reloadedRepository = CreateJsonWorkspaceSessionRepository(tempDir);
            var document = await reloadedRepository.LoadBranchDocumentAsync(session.Id, branch.Id, TestCancellationToken);
            Assert.NotNull(document);
            Assert.Contains(document.Events, e => EventType(e) == BranchEventTypes.BranchCreated);
            Assert.Contains(document.Events, e => EventType(e) == EventTypes.MessageTurn.MESSAGE_TURN_STARTED);
            Assert.All(document.Events, e =>
            {
                Assert.Equal(session.Id, e.SessionId);
                Assert.Equal(branch.Id, e.BranchId);
            });
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
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var client = new FakeChatClient();
        client.EnqueueTextResponse("hello human");
        var agent = CreateAgent(config, client);
        var sessionId = "session-1";

        await agent.CreateSessionAsync(sessionId, cancellationToken: TestCancellationToken);
        await agent.RunAsync("who are you", sessionId, "main", cancellationToken: TestCancellationToken);

        var document = await repository.LoadBranchDocumentAsync(sessionId, "main", TestCancellationToken);
        Assert.NotNull(document);

        var textDeltas = document.Events.OfType<TextDeltaEvent>().ToList();
        Assert.Single(textDeltas.Where(e => e.Text == "who are you"));
        Assert.Single(textDeltas.Where(e => e.Text == "hello human"));

        var loaded = await repository.LoadBranchAsync(sessionId, "main", TestCancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("who are you", loaded.Messages[0].Text);
        Assert.Equal("hello human", loaded.Messages[1].Text);
    }

    [Fact]
    public async Task Agent_PersistsTurnFailed_WhenSessionTurnFaults()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        var client = new FakeChatClient();
        var agent = CreateAgent(config, client);
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");

        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync(session.Id, branch);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in agent.RunTurnStreamAsync(
                [new ChatMessage(ChatRole.User, "fail please")],
                session,
                branch,
                cancellationToken: TestCancellationToken))
            {
            }
        });

        Assert.Contains("No responses queued", ex.Message);

        var document = await repository.LoadBranchDocumentAsync(session.Id, branch.Id, TestCancellationToken);
        var failed = Assert.IsType<MessageTurnErrorEvent>(Assert.Single(document!.Events.Where(e => EventType(e) == EventTypes.MessageTurn.MESSAGE_TURN_ERROR)));

        Assert.False(string.IsNullOrWhiteSpace(failed.MessageTurnId));
        Assert.Equal("session-1", failed.ConversationId);
        Assert.Equal(nameof(InvalidOperationException), failed.ErrorType);
        Assert.Contains("No responses queued", failed.Message);
    }

    [Fact]
    public async Task Agent_PersistsReasoningEvents_WhenReasoningExcludedFromModelHistory()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
        config.IncludeReasoningInModelHistory = false;
        var client = new FakeChatClient();
        client.EnqueueReasoningThenTextResponse("private thought", "public answer", "protected-reasoning");
        client.EnqueueTextResponse("second answer");
        var agent = CreateAgent(config, client);
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");

        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync(session.Id, branch);

        await foreach (var _ in agent.RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, "think then answer")],
            session,
            branch,
            cancellationToken: TestCancellationToken))
        {
        }

        var document = await repository.LoadBranchDocumentAsync(session.Id, branch.Id, TestCancellationToken);
        Assert.NotNull(document);

        var reasoningEvents = document.Events
            .Where(e => EventType(e) is EventTypes.Reasoning.REASONING_MESSAGE_START
                or EventTypes.Reasoning.REASONING_DELTA
                or EventTypes.Reasoning.REASONING_MESSAGE_END)
            .ToList();

        Assert.Equal(
            [
                EventTypes.Reasoning.REASONING_MESSAGE_START,
                EventTypes.Reasoning.REASONING_DELTA,
                EventTypes.Reasoning.REASONING_MESSAGE_END
            ],
            reasoningEvents.Select(EventType).ToArray());

        var deltaData = Assert.IsType<ReasoningDeltaEvent>(reasoningEvents[1]);
        Assert.Equal("private thought", deltaData.Text);
        Assert.Equal("protected-reasoning", deltaData.ProtectedData);

        var loaded = await repository.LoadBranchAsync(session.Id, branch.Id, TestCancellationToken);
        Assert.NotNull(loaded);
        var assistantMessage = Assert.Single(loaded.Messages.Where(m => m.Role == ChatRole.Assistant));
        var committedReasoning = Assert.Single(assistantMessage.Contents.OfType<TextReasoningContent>());
        Assert.Equal("private thought", committedReasoning.Text);
        Assert.Equal("protected-reasoning", committedReasoning.ProtectedData);
        Assert.Equal("public answer", assistantMessage.Text);

        await foreach (var _ in agent.RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, "second turn")],
            session,
            branch,
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
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var config = DefaultConfig();
        config.SessionRepository = repository;
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
        var branch = session.CreateBranch("main");

        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync(session.Id, branch);

        await foreach (var _ in agent.RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, "what is 2+2?")],
            session,
            branch,
            cancellationToken: TestCancellationToken))
        {
        }

        var document = await repository.LoadBranchDocumentAsync(session.Id, branch.Id, TestCancellationToken);
        Assert.NotNull(document);

        var toolEventTypes = document.Events
            .Where(e => EventType(e) is EventTypes.Tool.TOOL_CALL_START
                or EventTypes.Tool.TOOL_CALL_ARGS
                or EventTypes.Tool.TOOL_CALL_RESULT
                or EventTypes.Tool.TOOL_CALL_END)
            .Select(EventType)
            .ToArray();

        Assert.Equal(
            [
                EventTypes.Tool.TOOL_CALL_START,
                EventTypes.Tool.TOOL_CALL_ARGS,
                EventTypes.Tool.TOOL_CALL_END,
                EventTypes.Tool.TOOL_CALL_RESULT
            ],
            toolEventTypes);

        var started = Assert.IsType<ToolCallStartEvent>(
            document.Events.Single(e => EventType(e) == EventTypes.Tool.TOOL_CALL_START));
        Assert.Equal("call-1", started.CallId);
        Assert.Equal("Calculator", started.Name);
    }

    private static ISessionRepository CreateJsonWorkspaceSessionRepository(string path)
        => new WorkspaceSessionRepository(new JsonWorkspaceStore(path));
}
