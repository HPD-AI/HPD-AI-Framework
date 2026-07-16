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
                ParentContext = null
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Background);
        result.Background.Should().NotBeNull();
        result.Background!.Status.Should().Be("background_unavailable");
        result.Background.SourceKind.Should().Be(BackgroundTaskSourceKind.SubAgent);
        result.Background.Name.Should().Be("Reviewer");
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

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(agent, subAgent, context, CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.ThreadId.Should().StartWith("subagent/reviewer/");

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread.Should().NotBeNull();
        childThread!.Messages.Should().HaveCount(parentThread.Messages.Count);
        childThread.Kind.Should().Be(ThreadKind.SubAgent);
        childThread.SubAgentName.Should().Be("Reviewer");
        childThread.ParentSessionId.Should().Be("parent-session");
        childThread.ParentThreadId.Should().Be("main");
        childThread.SessionPolicy.Should().Be(nameof(SubAgentSessionPolicy.ParentSession));
        childThread.ThreadPolicy.Should().Be(nameof(SubAgentThreadPolicy.ForkFromParentThread));
        childThread.Visibility.Should().Be(ThreadVisibility.Hidden);
        childThread.Metadata["purpose"].Should().Be("review-current-thread");
        childThread.Metadata.Should().NotContainKey("kind");
        childThread.Metadata.Should().NotContainKey("parentThreadId");

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

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(agent, subAgent, context, CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.ThreadId.Should().StartWith("subagent/researcher/");

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread.Should().NotBeNull();
        childThread!.Messages.Should().BeEmpty();
        childThread.Kind.Should().Be(ThreadKind.SubAgent);
        childThread.ThreadPolicy.Should().Be(nameof(SubAgentThreadPolicy.FreshThread));
        childThread.Metadata.Should().NotContainKey("kind");
    }

    [Fact]
    public async Task ForkFromParentThread_WithThreadCompactionEnabled_CompactsSubAgentThread()
    {
        var store = new InMemorySessionStore();
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 2);
        var agent = await BuildAgentAsync(
            store,
            new CompactionMiddleware
            {
                Strategy = strategy,
                Config = new CompactionConfig
                {
                    ForkCompaction = ThreadForkCompactionOptions.Disabled,
                    Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 2 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        for (var i = 0; i < 4; i++)
        {
            parentThread.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedThread(ThreadForkCompactionOptions.Enabled));

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-2", "message-3");
        strategy.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ForkFromParentThread_WithThreadCompactionDisabled_SkipsGlobalForkCompaction()
    {
        var store = new InMemorySessionStore();
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 1);
        var agent = await BuildAgentAsync(
            store,
            new CompactionMiddleware
            {
                Strategy = strategy,
                Config = new CompactionConfig
                {
                    ForkCompaction = ThreadForkCompactionOptions.Enabled,
                    Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        for (var i = 0; i < 3; i++)
        {
            parentThread.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedThread(ThreadForkCompactionOptions.Disabled));

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
        strategy.CallCount.Should().Be(0);
    }

     [Fact]
    public async Task ForkFromParentThread_WithThreadCompactionStrategyOverride_UsesOverrideForSubAgentThread()
    {
        var store = new InMemorySessionStore();
        var defaultStrategy = new RetainLastMessagesCompactionStrategy(retainCount: 1);
        var overrideStrategy = new RetainLastMessagesCompactionStrategy(retainCount: 3);
        var agent = await BuildAgentAsync(
            store,
            new CompactionMiddleware
            {
                Strategy = defaultStrategy,
                StrategyFactory = (options, _) => options is MessageCountingCompactionOptions { PreserveRecentUserTurnCount: 3 }
                    ? overrideStrategy
                    : defaultStrategy,
                Config = new CompactionConfig
                {
                    ForkCompaction = ThreadForkCompactionOptions.Disabled,
                    Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentThread = (await store.ProjectThreadAsync("parent-session", "main", ThreadProjectionPurpose.ThreadHistory))!;
        for (var i = 0; i < 5; i++)
        {
            parentThread.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await AppendMessagesAsync(store, parentThread);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current thread.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedThread(new ThreadForkCompactionOptions
            {
                Mode = ThreadForkCompactionMode.Enabled,
                Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 3 }
            }));

        var route = await SubAgentRuntime.ResolveInvocationRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childThread = await store.ProjectThreadAsync(route.SessionId, route.ThreadId, ThreadProjectionPurpose.ThreadHistory);
        childThread!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-2", "message-3", "message-4");
        defaultStrategy.CallCount.Should().Be(0);
        overrideStrategy.CallCount.Should().Be(1);
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
                new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = retainCount }));
        }
    }
}
