using FluentAssertions;
using HPD.Events;
using HPD.Events.Core;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Tests.SubAgents;

public class SubAgentRuntimeTests
{
    private static AgentConfig MinimalConfig() => new()
    {
        Name = "SubAgentUnderTest",
        SystemInstructions = "Test sub-agent.",
        Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } }
    };

    [Fact]
    public async Task InvokeAsync_BackgroundOnly_WithoutRuntime_ReturnsUnavailableReceipt()
    {
        var subAgent = SubAgent.FromConfig(
            "test/reviewer",
            "Reviewer",
            "Reviews in the background.",
            MinimalConfig(),
            contextPolicy: SubAgentContextPolicy.Handoff,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.BackgroundOnly,
            operationNotification: null);

        var result = await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "review this",
                CapabilityId = CapabilityId.Create("test:reviewer"),
                ParentContext = null
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Synchronous);
        result.Operation.Should().BeNull();
        result.Text.Should().StartWith("background_unavailable:");
    }

    [Fact]
    public async Task InvokeAsync_UsesCapabilityIdentityWithoutModelAuthoredTaskName()
    {
        var subAgent = SubAgent.FromConfig(
            "test/reviewer",
            "Reviewer",
            "Reviews in the background.",
            MinimalConfig(),
            contextPolicy: SubAgentContextPolicy.Handoff,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.BackgroundOnly,
            operationNotification: null);

        var result = await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "review this",
                CapabilityId = CapabilityId.Create("test:reviewer"),
                ParentContext = null
            },
            CancellationToken.None);

        result.Text.Should().StartWith("background_unavailable:");
    }

    [Fact]
    public async Task InvokeAsync_DoesNotValidateRemovedTaskNameVocabulary()
    {
        var subAgent = SubAgent.FromConfig(
            "test/reviewer",
            "Reviewer",
            "Reviews in the background.",
            MinimalConfig(),
            contextPolicy: SubAgentContextPolicy.Handoff,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.BackgroundOnly,
            operationNotification: null);

        var result = await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "review this",
                CapabilityId = CapabilityId.Create("test:reviewer"),
                ParentContext = null
            },
            CancellationToken.None);

        result.Text.Should().StartWith("background_unavailable:");
    }

    [Fact]
    public async Task InvokeAsync_ReturnsOnlyThePersistedAssistantResponse()
    {
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Review complete.");
        var agent = await BuildAgentAsync(store, client);
        await agent.CreateSessionAsync("parent-session");
        var parentThread = (await store.ProjectThreadAsync(
            "parent-session",
            "main",
            ThreadProjectionPurpose.ThreadHistory))!;
        parentThread.AddMessage(new ChatMessage(ChatRole.User, "Please review this input."));
        await AppendMessagesAsync(store, parentThread);
        var services = new ServiceCollection()
            .AddSingleton<IAgentRuntimeResolver>(new FixedAgentRuntimeResolver(agent))
            .BuildServiceProvider();
        var context = await CreateFunctionContextAsync(store, "parent-session", "main", services);
        var subAgent = SubAgent.FromConfig(
            "test/reviewer",
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig());

        var result = await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "Review this input.",
                CapabilityId = CapabilityId.Create("test:reviewer"),
                ParentContext = context
            },
            CancellationToken.None);

        result.Text.Should().Be("Review complete.");
    }

    [Fact]
    public async Task InvokeAsync_WithoutAssistantResponse_FailsInsteadOfEchoingInput()
    {
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var client = new FakeChatClient();
        client.EnqueueTextResponse("");
        var agent = await BuildAgentAsync(store, client);
        await agent.CreateSessionAsync("parent-session");
        var parentThread = (await store.ProjectThreadAsync(
            "parent-session",
            "main",
            ThreadProjectionPurpose.ThreadHistory))!;
        parentThread.AddMessage(new ChatMessage(ChatRole.Assistant, "Inherited parent response."));
        await AppendMessagesAsync(store, parentThread);
        var services = new ServiceCollection()
            .AddSingleton<IAgentRuntimeResolver>(new FixedAgentRuntimeResolver(agent))
            .BuildServiceProvider();
        var context = await CreateFunctionContextAsync(store, "parent-session", "main", services);
        var subAgent = SubAgent.FromConfig(
            "test/reviewer",
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig());

        Func<Task> act = async () => await SubAgentRuntime.InvokeAsync(
            new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = subAgent,
                Input = "Do not echo this input.",
                CapabilityId = CapabilityId.Create("test:reviewer"),
                ParentContext = context
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*completed without an assistant response*");
    }

    [Fact]
    public async Task DefaultPolicy_HandsOffText_WithSubAgentMetadata()
    {
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var agent = await BuildAgentAsync(store);
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        parentThread.AddMessage(new ChatMessage(ChatRole.User, "Parent context"));
        parentThread.AddMessage(new ChatMessage(ChatRole.Assistant, "Parent answer"));
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "test/reviewer",
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig(),
            contextPolicy: SubAgentContextPolicy.Handoff,
            metadata: new Dictionary<string, object> { ["purpose"] = "review-current-thread" });

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(
            agent, subAgent, context, "review-storage", CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.ThreadId.Should().StartWith("subagent/reviewer/review-storage/");

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread.Should().NotBeNull();
        childThread!.Messages.Should().ContainSingle();
        childThread.Messages[0].Text.Should().Contain("Parent context").And.Contain("Parent answer");
        childThread.ForkedFrom.Should().BeNull();
        childThread.MiddlewareState.Should().BeEmpty();
        childThread.Kind.Should().Be(ThreadKind.SubAgent);
        childThread.SubAgentName.Should().Be("Reviewer");
        childThread.ParentSessionId.Should().Be("parent-session");
        childThread.ParentThreadId.Should().Be("main");
        childThread.Visibility.Should().Be(ThreadVisibility.Hidden);
        childThread.Metadata["purpose"].Should().Be("review-current-thread");
        childThread.Metadata.Should().NotContainKey("kind");
        childThread.Metadata.Should().NotContainKey("parentThreadId");

        var descriptor = await store.GetThreadAsync(new ThreadKey(route.SessionId, route.ThreadId));
        descriptor!.RuntimeChild!.SubAgentName.Should().Be("Reviewer");

        await store.AppendThreadEventsAsync(
            new ThreadKey(route.SessionId, route.ThreadId),
            [Scope(route, new ThreadExecutionStartedEvent("child-run", "Reviewer", DateTimeOffset.UtcNow))]);
        descriptor = await store.GetThreadAsync(new ThreadKey(route.SessionId, route.ThreadId));
        descriptor!.RuntimeChild!.Status.Should().Be(ThreadExecutionStatus.Active);

        await store.AppendThreadEventsAsync(
            new ThreadKey(route.SessionId, route.ThreadId),
            [Scope(route, new ThreadExecutionFinishedEvent(
                "child-run",
                "Reviewer",
                ThreadExecutionOutcome.Failed,
                DateTimeOffset.UtcNow,
                new ThreadExecutionError("TestFailure", "Test failure.")))]);
        descriptor = await store.GetThreadAsync(new ThreadKey(route.SessionId, route.ThreadId));
        descriptor!.RuntimeChild!.Status.Should().Be(ThreadExecutionStatus.Failed);

        context.ResultMetadata.TryGet<string>("subAgentStatus", out var status).Should().BeTrue();
        status.Should().Be("started");
        context.ResultMetadata.TryGet<string>("subAgentThreadId", out var resultThreadId).Should().BeTrue();
        resultThreadId.Should().Be(route.ThreadId);
    }

    [Fact]
    public async Task FreshThread_CreatesEmptyThreadInParentSession()
    {
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var agent = await BuildAgentAsync(store);
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        parentThread.AddMessage(new ChatMessage(ChatRole.User, "This should not be copied"));
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "test/researcher",
            "Researcher",
            "Starts without parent conversation history.",
            MinimalConfig(),
            SubAgentContextPolicy.Fresh);

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(
            agent, subAgent, context, "fresh-research", CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.ThreadId.Should().StartWith("subagent/researcher/fresh-research/");

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread.Should().NotBeNull();
        childThread!.Messages.Should().BeEmpty();
        childThread.Kind.Should().Be(ThreadKind.SubAgent);
        childThread.Metadata.Should().NotContainKey("kind");
    }

    private static async Task<Agent> BuildAgentAsync(
        InMemorySessionStore store,
        params IAgentMiddleware[] middlewares)
        => await BuildAgentAsync(store, new FakeChatClient(), middlewares);

    private static async Task<Agent> BuildAgentAsync(
        InMemorySessionStore store,
        FakeChatClient client,
        params IAgentMiddleware[] middlewares)
    {
        var config = MinimalConfig();
        config.Clients.Chat!.Override = ClientOverride<IChatClient>.Borrow(client, "test", "local");
        var builder = new AgentBuilder(config, new TestProviderRegistry(client))
            .WithSessionStore(store);

        foreach (var middleware in middlewares)
            builder.WithMiddleware(middleware);

        return await builder.BuildAsync(CancellationToken.None);
    }

    private static async Task<FunctionExecutionContext> CreateFunctionContextAsync(
        InMemorySessionStore store,
        string sessionId,
        string threadId,
        IServiceProvider? services = null)
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
            CancellationToken.None,
            services: services);
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

    private sealed class FixedAgentRuntimeResolver(Agent agent) : IAgentRuntimeResolver
    {
        public Task<IAgentRuntimeLease> GetOrBuildAsync(
            string agentId,
            string sessionId,
            string threadId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IAgentRuntimeLease>(new Lease(agent));

        private sealed class Lease(Agent agent) : IAgentRuntimeLease
        {
            public Agent Agent { get; } = agent;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

}
