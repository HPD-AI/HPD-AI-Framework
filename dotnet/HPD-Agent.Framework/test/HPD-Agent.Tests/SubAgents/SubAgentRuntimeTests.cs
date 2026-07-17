using FluentAssertions;
using HPD.Events;
using HPD.Events.Core;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public class SubAgentRuntimeTests
{
    private static AgentConfig MinimalConfig() => new()
    {
        Name = "SubAgentUnderTest",
        SystemInstructions = "Test sub-agent.",
        Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } }
    };

    [Fact]
    public async Task InvokeAsync_BackgroundOnly_WithoutRuntime_ReturnsUnavailableReceipt()
    {
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews in the background.",
            MinimalConfig(),
            executionPolicy: null,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.BackgroundOnly,
            backgroundNotification: null);

        var result = await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "review this",
                TaskName = "review-current-change",
                ParentContext = null
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Background);
        result.Background.Should().NotBeNull();
        result.Background!.Status.Should().Be("background_unavailable");
        result.Background.SourceKind.Should().Be(BackgroundTaskSourceKind.SubAgent);
        result.Background.Name.Should().Be("review-current-change");
    }

    [Fact]
    public async Task InvokeAsync_AllowsAgentAsTaskName()
    {
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews in the background.",
            MinimalConfig(),
            executionPolicy: null,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.BackgroundOnly,
            backgroundNotification: null);

        var result = await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "review this",
                TaskName = "agent",
                ParentContext = null
            },
            CancellationToken.None);

        result.Background!.Name.Should().Be("agent");
    }

    [Fact]
    public async Task InvokeAsync_RejectsTaskNameWithoutLettersOrNumbers()
    {
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews in the background.",
            MinimalConfig(),
            executionPolicy: null,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.BackgroundOnly,
            backgroundNotification: null);

        Func<Task> act = async () => await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "review this",
                TaskName = "---",
                ParentContext = null
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*at least one letter or number*");
    }

    [Fact]
    public async Task DefaultPolicy_ForksParentThread_WithSubAgentMetadata()
    {
        var store = new InMemorySessionStore();
        var agent = await BuildAgentAsync(store);
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        parentThread.AddMessage(new ChatMessage(ChatRole.User, "Parent context"));
        parentThread.AddMessage(new ChatMessage(ChatRole.Assistant, "Parent answer"));
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig(),
            executionPolicy: null,
            metadata: new Dictionary<string, object> { ["purpose"] = "review-current-thread" });

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(
            agent, subAgent, context, "review-storage", CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.ThreadId.Should().StartWith("subagent/reviewer/review-storage/");

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread.Should().NotBeNull();
        childThread!.Messages.Should().HaveCount(parentThread.Messages.Count);
        childThread.Kind.Should().Be(ThreadKind.SubAgent);
        childThread.SubAgentName.Should().Be("Reviewer");
        childThread.SubAgentTaskName.Should().Be("review-storage");
        childThread.ParentSessionId.Should().Be("parent-session");
        childThread.ParentThreadId.Should().Be("main");
        childThread.SessionPolicy.Should().Be(nameof(SubAgentSessionPolicy.ParentSession));
        childThread.ThreadPolicy.Should().Be(nameof(SubAgentThreadPolicy.ForkFromParentThread));
        childThread.Visibility.Should().Be(ThreadVisibility.Hidden);
        childThread.Metadata["purpose"].Should().Be("review-current-thread");
        childThread.Metadata.Should().NotContainKey("kind");
        childThread.Metadata.Should().NotContainKey("parentThreadId");

        var descriptor = await store.GetThreadAsync(new ThreadKey(route.SessionId, route.ThreadId));
        descriptor!.RuntimeChild!.SubAgentName.Should().Be("Reviewer");
        descriptor.RuntimeChild.SubAgentTaskName.Should().Be("review-storage");

        await store.AppendThreadEventsAsync(
            new ThreadKey(route.SessionId, route.ThreadId),
            [Scope(route, new ThreadRunStartedEvent("child-run", "Reviewer", DateTimeOffset.UtcNow))]);
        descriptor = await store.GetThreadAsync(new ThreadKey(route.SessionId, route.ThreadId));
        descriptor!.RuntimeChild!.Status.Should().Be(ThreadRunStatus.Active);

        await store.AppendThreadEventsAsync(
            new ThreadKey(route.SessionId, route.ThreadId),
            [Scope(route, new ThreadRunCompletedEvent("child-run", "Reviewer", false, "TestFailure"))]);
        descriptor = await store.GetThreadAsync(new ThreadKey(route.SessionId, route.ThreadId));
        descriptor!.RuntimeChild!.Status.Should().Be(ThreadRunStatus.Failed);

        context.ResultMetadata.TryGet<string>("subAgentStatus", out var status).Should().BeTrue();
        status.Should().Be("started");
        context.ResultMetadata.TryGet<string>("subAgentThreadId", out var resultThreadId).Should().BeTrue();
        resultThreadId.Should().Be(route.ThreadId);
    }

    [Fact]
    public async Task FreshThread_CreatesEmptyThreadInParentSession()
    {
        var store = new InMemorySessionStore();
        var agent = await BuildAgentAsync(store);
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        parentThread.AddMessage(new ChatMessage(ChatRole.User, "This should not be copied"));
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Researcher",
            "Starts without parent conversation history.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionFreshThread());

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(
            agent, subAgent, context, "fresh-research", CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.ThreadId.Should().StartWith("subagent/researcher/fresh-research/");

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread.Should().NotBeNull();
        childThread!.Messages.Should().BeEmpty();
        childThread.Kind.Should().Be(ThreadKind.SubAgent);
        childThread.ThreadPolicy.Should().Be(nameof(SubAgentThreadPolicy.FreshThread));
        childThread.Metadata.Should().NotContainKey("kind");
    }

    private static async Task<Agent> BuildAgentAsync(
        InMemorySessionStore store,
        params IAgentMiddleware[] middlewares)
    {
        var builder = new AgentBuilder(MinimalConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store);

        foreach (var middleware in middlewares)
            builder.WithMiddleware(middleware);

        return await builder.BuildAsync(CancellationToken.None);
    }

    private static async Task<FunctionExecutionContext> CreateFunctionContextAsync(
        InMemorySessionStore store,
        string sessionId,
        string threadId)
    {
        var function = AIFunctionFactory.Create(
            (string query) => query,
            new AIFunctionFactoryOptions { Name = "call_subagent", Description = "Calls a subagent." });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "ParentAgent");
        var session = (await store.LoadSessionAsync(sessionId))!;
        session.Store = store;
        var thread = (await store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory))!;
        var agentContext = new AgentContext(
            "ParentAgent",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "tool-call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);

        return new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = "tool-call-1",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            });
    }

    private static async Task AppendMessagesAsync(InMemorySessionStore store, HPD.Agent.Thread thread)
    {
        var events = thread.Messages
            .Select(message => ThreadEventFactory.ContentAdded(
                thread.SessionId,
                thread.Id,
                message,
                message.Contents[0]))
            .ToList();
        await store.AppendThreadEventsAsync(
            new ThreadKey(thread.SessionId, thread.Id),
            events,
            ThreadAppendCondition.Any);
    }

    private static AgentEvent Scope(SubAgentRuntime.SubAgentInvocationRoute route, AgentEvent evt)
        => evt with
        {
            EventId = Guid.NewGuid().ToString("N"),
            SessionId = route.SessionId,
            ThreadId = route.ThreadId
        };

}
