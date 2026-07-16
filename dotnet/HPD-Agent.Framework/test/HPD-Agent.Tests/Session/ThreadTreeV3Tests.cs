using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Thread tree tests for durable lineage and semantic fork group projection.
/// </summary>
public class ThreadTreeV3Tests : AgentTestBase
{
    private static async Task<Agent> CreateAgentWithStore(InMemorySessionStore store)
        => await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);

    [Fact]
    public void ThreadLineage_SerializesAndDeserializes()
    {
        var thread = new Thread(
            id: "fork-1",
            sessionId: "session-1",
            messages: [],
            forkedFrom: "main",
            forkedAtMessageId: "msg-1",
            forkedAtMessageIndex: 0,
            createdAt: DateTime.UtcNow,
            lastActivity: DateTime.UtcNow,
            name: "Fork 1",
            description: null,
            tags: ["experiment"],
            ancestors: new Dictionary<string, string> { ["0"] = "main" },
            middlewareState: [],
            metadata: new Dictionary<string, object> { ["surface"] = "test" },
            childThreads: ["fork-1a"]);

        var json = System.Text.Json.JsonSerializer.Serialize(thread);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Thread>(json);

        deserialized.Should().NotBeNull();
        deserialized!.ForkedFrom.Should().Be("main");
        deserialized.ForkedAtMessageId.Should().Be("msg-1");
        deserialized.ForkedAtMessageIndex.Should().Be(0);
        deserialized.Ancestors.Should().ContainKey("0");
        deserialized.ChildThreads.Should().Equal("fork-1a");
        deserialized.Tags.Should().Equal("experiment");
    }

    [Fact]
    public void RootThread_DefaultsToLineageOnlyState()
    {
        var thread = new Thread("session-1", "main");

        thread.ValidateTreeInvariants();

        thread.ForkedFrom.Should().BeNull();
        thread.ForkedAtMessageId.Should().BeNull();
        thread.ForkedAtMessageIndex.Should().BeNull();
        thread.ChildThreads.Should().BeEmpty();
        thread.TotalForks.Should().Be(0);
    }

    [Fact]
    public void ThreadCannotForkFromItself()
    {
        var thread = new Thread(
            id: "main",
            sessionId: "session-1",
            messages: [],
            forkedFrom: "main",
            forkedAtMessageId: "msg-1",
            forkedAtMessageIndex: 0,
            createdAt: DateTime.UtcNow,
            lastActivity: DateTime.UtcNow,
            name: null,
            description: null,
            tags: null,
            ancestors: null,
            middlewareState: [],
            metadata: null,
            childThreads: []);

        var act = () => thread.ValidateTreeInvariants();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ForkedFrom cannot reference itself*");
    }

    [Fact]
    public async Task ForkThread_SetsLineageAndParentChildReference()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;

        var fork = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);

        var reloadedMain = await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory);
        var reloadedFork = await store.ProjectThreadAsync(session.Id, "fork-1", ThreadProjectionPurpose.ThreadHistory);

        fork.Id.Should().Be("fork-1");
        reloadedMain!.ChildThreads.Should().Equal("fork-1");
        reloadedMain.TotalForks.Should().Be(1);
        reloadedFork!.ForkedFrom.Should().Be("main");
        reloadedFork.ForkedAtMessageId.Should().Be(main.Messages[0].MessageId);
        reloadedFork.ForkedAtMessageIndex.Should().Be(0);
        reloadedFork.Ancestors.Should().Contain("0", "main");
    }

    [Fact]
    public async Task MultipleForksAtSamePoint_StoreOnlyLineageFacts()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;
        var forkMessageId = main.Messages[0].MessageId!;

        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: forkMessageId);
        main = (await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-2", fromMessageId: forkMessageId);

        var reloadedMain = await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory);
        var fork1 = await store.ProjectThreadAsync(session.Id, "fork-1", ThreadProjectionPurpose.ThreadHistory);
        var fork2 = await store.ProjectThreadAsync(session.Id, "fork-2", ThreadProjectionPurpose.ThreadHistory);

        reloadedMain!.ChildThreads.Should().Equal("fork-1", "fork-2");
        fork1!.ForkedFrom.Should().Be("main");
        fork2!.ForkedFrom.Should().Be("main");
        fork1.ForkedAtMessageId.Should().Be(forkMessageId);
        fork2.ForkedAtMessageId.Should().Be(forkMessageId);
    }

    [Fact]
    public async Task ForkGraph_GroupsNestedForksAtSameCopiedMessage()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(new ChatMessage(ChatRole.Assistant, "Response 1") { MessageId = "assistant-1" });
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;
        var forkMessageId = main.Messages[0].MessageId!;

        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: forkMessageId);
        await agent.ForkThreadAsync(fork1, "fork-2", fromMessageId: forkMessageId);

        var threads = await LoadThreadsAsync(store, session.Id, "main", "fork-1", "fork-2");
        var group = ThreadForkGraph.BuildVisibleForkGroups(threads).Should().ContainSingle().Subject;

        group.SourceThreadId.Should().Be("main");
        group.ForkedAtMessageId.Should().Be(forkMessageId);
        group.Members.Select(member => member.Thread.Id)
            .Should().Equal("main", "fork-1", "fork-2");
    }

    [Fact]
    public async Task ForkGraph_GroupsNestedRootForksTogether()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;

        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: null);
        await agent.ForkThreadAsync(fork1, "fork-2", fromMessageId: null);

        var threads = await LoadThreadsAsync(store, session.Id, "main", "fork-1", "fork-2");
        var group = ThreadForkGraph.BuildVisibleForkGroups(threads).Should().ContainSingle().Subject;

        group.SourceThreadId.Should().Be("main");
        group.ForkedAtMessageId.Should().BeNull();
        group.Members.Select(member => member.Thread.Id)
            .Should().Equal("main", "fork-1", "fork-2");
    }

    [Fact]
    public void ForkGraph_AnchorsRevisionForkMemberToInputMessageMetadata()
    {
        var main = ThreadWithMessages(
            "main",
            UserMessage("First", "user-1"),
            AssistantMessage("First answer", "assistant-1"),
            UserMessage("Second", "user-2"),
            AssistantMessage("Second answer", "assistant-2"));
        var fork = ThreadWithMessages(
            "fork-1",
            forkedFrom: "main",
            forkedAtMessageId: "user-2",
            forkedAtMessageIndex: 2,
            ancestors: new Dictionary<string, string> { ["0"] = "main" },
            metadata: new Dictionary<string, object> { ["inputMessageId"] = "user-2" },
            messages:
            [
                UserMessage("First", "user-1"),
                AssistantMessage("First answer", "assistant-1"),
                UserMessage("Edited second", "user-2"),
                AssistantMessage("Edited answer", "assistant-edited")
            ]);

        var group = ThreadForkGraph.BuildVisibleForkGroups([main, fork])
            .Should().ContainSingle().Subject;

        var forkMember = group.Members.Should()
            .ContainSingle(member => member.Thread.Id == "fork-1").Subject;
        forkMember.ChoiceMessageId.Should().Be("user-2");
        forkMember.ChoiceMessageIndex.Should().Be(2);
    }

    [Fact]
    public void ForkGraph_AnchorsBoundaryForkMemberToFirstUserMessageAtOrAfterBoundary()
    {
        var main = ThreadWithMessages(
            "main",
            UserMessage("First", "user-1"),
            AssistantMessage("First answer", "assistant-1"),
            UserMessage("Second", "user-2"),
            AssistantMessage("Second answer", "assistant-2"));
        var fork = ThreadWithMessages(
            "fork-1",
            forkedFrom: "main",
            forkedAtMessageId: "assistant-1",
            forkedAtMessageIndex: 1,
            ancestors: new Dictionary<string, string> { ["0"] = "main" },
            metadata: null,
            messages:
            [
                UserMessage("First", "user-1"),
                AssistantMessage("First answer", "assistant-1"),
                UserMessage("Replacement second", "replacement-user-2"),
                AssistantMessage("Replacement answer", "assistant-replacement")
            ]);

        var group = ThreadForkGraph.BuildVisibleForkGroups([main, fork])
            .Should().ContainSingle().Subject;

        group.ForkedAtMessageId.Should().Be("assistant-1");
        var sourceMember = group.Members.Should()
            .ContainSingle(member => member.Thread.Id == "main").Subject;
        var forkMember = group.Members.Should()
            .ContainSingle(member => member.Thread.Id == "fork-1").Subject;
        sourceMember.ChoiceMessageId.Should().Be("user-2");
        sourceMember.ChoiceMessageIndex.Should().Be(2);
        forkMember.ChoiceMessageId.Should().Be("replacement-user-2");
        forkMember.ChoiceMessageIndex.Should().Be(2);
    }

    [Fact]
    public async Task ForkAtDifferentMessages_CapturesDifferentForkPoints()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(new ChatMessage(ChatRole.Assistant, "Response 1") { MessageId = "assistant-1" });
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;

        await agent.ForkThreadAsync(main, "fork-at-user", fromMessageId: main.Messages[0].MessageId!);
        main = (await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-at-assistant", fromMessageId: "assistant-1");

        var forkAtUser = await store.ProjectThreadAsync(session.Id, "fork-at-user", ThreadProjectionPurpose.ThreadHistory);
        var forkAtAssistant = await store.ProjectThreadAsync(session.Id, "fork-at-assistant", ThreadProjectionPurpose.ThreadHistory);

        forkAtUser!.ForkedAtMessageIndex.Should().Be(0);
        forkAtAssistant!.ForkedAtMessageIndex.Should().Be(1);
        forkAtUser.ForkedAtMessageId.Should().NotBe(forkAtAssistant.ForkedAtMessageId);
    }

    [Fact]
    public async Task ForkThread_ExpandsCopiedPrefixThroughToolTurn()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var turnId = "turn-with-tool";
        var main = session.CreateThread("main");
        main.AddMessage(new ChatMessage(ChatRole.User, "Use a tool") { MessageId = "user-1" });
        main.AddMessage(MessageWithTurn(
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "Lookup")])
            {
                MessageId = "assistant-tool-call"
            },
            turnId));
        main.AddMessage(MessageWithTurn(
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("call-1", "tool result")])
            {
                MessageId = "tool-result"
            },
            turnId));
        main.AddMessage(MessageWithTurn(
            new ChatMessage(ChatRole.Assistant, "The answer from the tool") { MessageId = "assistant-final" },
            turnId));
        main.AddMessage(new ChatMessage(ChatRole.User, "Different turn") { MessageId = "user-2" });
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;

        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: "assistant-tool-call");

        var fork = await store.ProjectThreadAsync(session.Id, "fork-1", ThreadProjectionPurpose.ThreadHistory);

        fork.Should().NotBeNull();
        fork!.ForkedAtMessageId.Should().Be("assistant-tool-call");
        fork.ForkedAtMessageIndex.Should().Be(1);
        fork.Messages.Select(message => message.MessageId)
            .Should().Equal("user-1", "assistant-tool-call", "tool-result", "assistant-final");
        fork.Messages[1].Contents.OfType<FunctionCallContent>()
            .Select(call => call.CallId)
            .Should().Contain("call-1");
        fork.Messages[2].Contents.OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .Should().Contain("call-1");
    }

    [Fact]
    public async Task ForkThread_CopiesSourceEventsInsteadOfSynthesizingToolHistory()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        await store.SaveInitialThreadAsync(session.Id, main);

        var turnId = "turn-with-rich-tool-events";
        await AppendToolTurnAsync(store, session.Id, main.Id, turnId);

        main = (await store.ProjectThreadAsync(session.Id, main.Id, ThreadProjectionPurpose.ThreadHistory))!;
        main.Session = session;

        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: "assistant-tool-call");

        var fork = await store.ProjectThreadAsync(session.Id, "fork-1", ThreadProjectionPurpose.ThreadHistory);
        var forkDocument = await store.CollectThreadEventsAsync(session.Id, "fork-1");
        var sourceDocument = await store.CollectThreadEventsAsync(session.Id, main.Id);

        fork.Should().NotBeNull();
        fork!.Messages.Select(message => message.MessageId)
            .Should().Equal("user-1", "assistant-tool-call", "tool-result", "assistant-final");

        var forkToolStart = forkDocument!.OfType<ToolCallStartEvent>()
            .Should().ContainSingle(evt => evt.CallId == "call-1").Subject;
        forkToolStart.ToolHarnessName.Should().Be("CodingToolHarness");
        forkToolStart.CallType.Should().Be(ToolCallType.Skill);
        forkToolStart.ThreadId.Should().Be("fork-1");

        var sourceToolStart = sourceDocument!.OfType<ToolCallStartEvent>()
            .Should().ContainSingle(evt => evt.CallId == "call-1").Subject;
        forkToolStart.EventId.Should().NotBe(sourceToolStart.EventId);

        var forkToolResult = forkDocument.OfType<ToolCallResultEvent>()
            .Should().ContainSingle(evt => evt.CallId == "call-1").Subject;
        forkToolResult.ToolHarnessName.Should().Be("CodingToolHarness");
        forkToolResult.CallType.Should().Be(ToolCallType.Skill);
        forkToolResult.Name.Should().Be("ListDirectory");
        forkToolResult.ThreadId.Should().Be("fork-1");

        forkDocument.OfType<ThreadCreatedEvent>()
            .Should().ContainSingle(evt => evt.ForkedFrom == "main");
    }

    [Fact]
    public async Task ForkThread_CopiesCoherentSourceEventPrefixThroughCompletedRun()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        await store.SaveInitialThreadAsync(session.Id, main);

        const string turnId = "turn-1";
        const string runtimeRunId = "run-1";
        await AppendCompletedTextRunAsync(store, session.Id, main.Id, runtimeRunId, turnId);

        main = (await store.ProjectThreadAsync(session.Id, main.Id, ThreadProjectionPurpose.ThreadHistory))!;
        main.Session = session;

        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: "assistant-1");

        var fork = await store.ProjectThreadAsync(session.Id, "fork-1", ThreadProjectionPurpose.ThreadHistory);
        var forkDocument = await store.CollectThreadEventsAsync(session.Id, "fork-1");

        fork.Should().NotBeNull();
        fork!.Messages.Select(message => message.MessageId)
            .Should().Equal("user-1", "assistant-1");

        forkDocument.Should().NotBeNull();
        forkDocument!.OfType<MessageTurnStartedEvent>()
            .Should().ContainSingle(evt => evt.MessageTurnId == turnId);
        forkDocument.OfType<MessageTurnFinishedEvent>()
            .Should().ContainSingle(evt => evt.MessageTurnId == turnId);
        forkDocument.OfType<ThreadRunStartedEvent>()
            .Should().ContainSingle(evt => evt.RuntimeRunId == runtimeRunId);
        forkDocument.OfType<ThreadRunCompletedEvent>()
            .Should().ContainSingle(evt => evt.RuntimeRunId == runtimeRunId);

        var copiedRunStart = forkDocument.OfType<ThreadRunStartedEvent>().Single();
        var copiedRunEnd = forkDocument.OfType<ThreadRunCompletedEvent>().Single();
        copiedRunStart.ThreadId.Should().Be("fork-1");
        copiedRunEnd.ThreadId.Should().Be("fork-1");
        copiedRunStart.ThreadSequenceNumber.Should().BeLessThan(copiedRunEnd.ThreadSequenceNumber);
    }

    private static async Task<IReadOnlyList<Thread>> LoadThreadsAsync(
        InMemorySessionStore store,
        string sessionId,
        params string[] threadIds)
    {
        var threads = new List<Thread>();
        foreach (var threadId in threadIds)
        {
            var thread = await store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory);
            thread.Should().NotBeNull();
            threads.Add(thread!);
        }

        return threads;
    }

    private static ChatMessage MessageWithTurn(ChatMessage message, string turnId)
    {
        message.AdditionalProperties ??= [];
        message.AdditionalProperties[ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = turnId;
        return message;
    }

    private static Thread ThreadWithMessages(
        string id,
        params ChatMessage[] messages)
        => ThreadWithMessages(
            id,
            forkedFrom: null,
            forkedAtMessageId: null,
            forkedAtMessageIndex: null,
            ancestors: null,
            metadata: null,
            messages: messages);

    private static Thread ThreadWithMessages(
        string id,
        string? forkedFrom,
        string? forkedAtMessageId,
        int? forkedAtMessageIndex,
        Dictionary<string, string>? ancestors,
        Dictionary<string, object>? metadata,
        IReadOnlyList<ChatMessage> messages)
        => new(
            id: id,
            sessionId: "session-1",
            messages: [.. messages],
            forkedFrom: forkedFrom,
            forkedAtMessageId: forkedAtMessageId,
            forkedAtMessageIndex: forkedAtMessageIndex,
            createdAt: DateTime.UtcNow,
            lastActivity: DateTime.UtcNow,
            name: id,
            description: null,
            tags: null,
            ancestors: ancestors,
            middlewareState: [],
            metadata: metadata,
            childThreads: []);

    private static ChatMessage UserMessage(string text, string messageId)
        => new(ChatRole.User, text) { MessageId = messageId };

    private static ChatMessage AssistantMessage(string text, string messageId)
        => new(ChatRole.Assistant, text) { MessageId = messageId };

    private static async Task AppendToolTurnAsync(
        InMemorySessionStore store,
        string sessionId,
        string threadId,
        string turnId)
    {
        var user = new ChatMessage(ChatRole.User, "Use a tool") { MessageId = "user-1" };
        var assistantTool = new ChatMessage(ChatRole.Assistant, []) { MessageId = "assistant-tool-call" };
        var tool = new ChatMessage(ChatRole.Tool, []) { MessageId = "tool-result" };
        var assistantFinal = new ChatMessage(ChatRole.Assistant, []) { MessageId = "assistant-final" };

        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TurnStarted(sessionId, threadId, turnId, sessionId, "agent-1", "Agent", 1, false));

        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageStarted(sessionId, threadId, turnId, "user-1", ChatRole.User.Value, 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextDelta(sessionId, threadId, turnId, "user-1", "Use a tool", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageCompleted(sessionId, threadId, turnId, "user-1", 0));

        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ToolCallStarted(
                sessionId,
                threadId,
                turnId,
                "call-1",
                "ListDirectory",
                "assistant-tool-call",
                "CodingToolHarness",
                ToolCallType.Skill,
                0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ToolCallArgs(sessionId, threadId, turnId, "call-1", """{"path":"/workspace"}""", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ToolCallCompleted(
                sessionId,
                threadId,
                turnId,
                "call-1",
                0,
                "assistant-tool-call",
                "ListDirectory",
                """{"path":"/workspace"}"""));

        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ToolCallResult(
                sessionId,
                threadId,
                turnId,
                "call-1",
                "tool-result",
                new ToolResultPayload(Text: "workspace files"),
                "CodingToolHarness",
                ToolCallType.Skill,
                0,
                "ListDirectory"));

        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageStarted(sessionId, threadId, turnId, "assistant-final", ChatRole.Assistant.Value, 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextDelta(sessionId, threadId, turnId, "assistant-final", "The answer from the tool", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageCompleted(sessionId, threadId, turnId, "assistant-final", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TurnCompleted(sessionId, threadId, turnId, sessionId, "agent-1", "Agent", 1, "done", TimeSpan.FromMilliseconds(10), 4));
    }

    private static async Task AppendCompletedTextRunAsync(
        InMemorySessionStore store,
        string sessionId,
        string threadId,
        string runtimeRunId,
        string turnId)
    {
        await store.AppendThreadEventAsync(sessionId, threadId,
            new ThreadRunStartedEvent(runtimeRunId, "agent-1", DateTimeOffset.UtcNow));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageStarted(sessionId, threadId, null, "user-1", ChatRole.User.Value, 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextDelta(sessionId, threadId, null, "user-1", "who are you", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageCompleted(sessionId, threadId, null, "user-1", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TurnStarted(sessionId, threadId, turnId, sessionId, "agent-1", "Agent", 1, false));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ReasoningStarted(sessionId, threadId, turnId, "assistant-1", ChatRole.Assistant.Value, 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ReasoningDelta(sessionId, threadId, turnId, "assistant-1", "I should introduce myself.", null, 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.ReasoningCompleted(sessionId, threadId, turnId, "assistant-1", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageStarted(sessionId, threadId, turnId, "assistant-1", ChatRole.Assistant.Value, 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextDelta(sessionId, threadId, turnId, "assistant-1", "I am HPD-OS.", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TextMessageCompleted(sessionId, threadId, turnId, "assistant-1", 0));
        await store.AppendThreadEventAsync(sessionId, threadId,
            ThreadEventFactory.TurnCompleted(sessionId, threadId, turnId, sessionId, "agent-1", "Agent", 1, "done", TimeSpan.FromMilliseconds(10), 2));
        await store.AppendThreadEventAsync(sessionId, threadId,
            new ThreadRunCompletedEvent(runtimeRunId, "agent-1", Cancelled: false));
    }
}
