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
    public async Task DefaultPolicy_ForksParentBranch_WithSubAgentMetadata()
    {
        var store = new InMemorySessionStore();
        var agent = await BuildAgentAsync(store);
        await agent.CreateSessionAsync("parent-session");

        var parentBranch = (await store.LoadBranchAsync("parent-session", "main"))!;
        parentBranch.AddMessage(new ChatMessage(ChatRole.User, "Parent context"));
        parentBranch.AddMessage(new ChatMessage(ChatRole.Assistant, "Parent answer"));
        await store.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            executionPolicy: null,
            metadata: new Dictionary<string, object> { ["purpose"] = "review-current-branch" });

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.BranchId.Should().StartWith("subagent/reviewer/");

        var childBranch = await store.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch.Should().NotBeNull();
        childBranch!.Messages.Should().HaveCount(parentBranch.Messages.Count);
        childBranch.Kind.Should().Be(BranchKind.SubAgent);
        childBranch.SubAgentName.Should().Be("Reviewer");
        childBranch.ParentSessionId.Should().Be("parent-session");
        childBranch.ParentBranchId.Should().Be("main");
        childBranch.SessionPolicy.Should().Be(nameof(SubAgentSessionPolicy.ParentSession));
        childBranch.BranchPolicy.Should().Be(nameof(SubAgentBranchPolicy.ForkFromParentBranch));
        childBranch.Visibility.Should().Be(BranchVisibility.Hidden);
        childBranch.Metadata["purpose"].Should().Be("review-current-branch");
        childBranch.Metadata.Should().NotContainKey("kind");
        childBranch.Metadata.Should().NotContainKey("parentBranchId");

        context.ResultMetadata.TryGet<string>("subAgentStatus", out var status).Should().BeTrue();
        status.Should().Be("started");
        context.ResultMetadata.TryGet<string>("subAgentBranchId", out var resultBranchId).Should().BeTrue();
        resultBranchId.Should().Be(route.BranchId);
    }

    [Fact]
    public async Task FreshBranch_CreatesEmptyBranchInParentSession()
    {
        var store = new InMemorySessionStore();
        var agent = await BuildAgentAsync(store);
        await agent.CreateSessionAsync("parent-session");

        var parentBranch = (await store.LoadBranchAsync("parent-session", "main"))!;
        parentBranch.AddMessage(new ChatMessage(ChatRole.User, "This should not be copied"));
        await store.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Researcher",
            "Starts without parent conversation history.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionFreshBranch());

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.BranchId.Should().StartWith("subagent/researcher/");

        var childBranch = await store.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch.Should().NotBeNull();
        childBranch!.Messages.Should().BeEmpty();
        childBranch.Kind.Should().Be(BranchKind.SubAgent);
        childBranch.BranchPolicy.Should().Be(nameof(SubAgentBranchPolicy.FreshBranch));
        childBranch.Metadata.Should().NotContainKey("kind");
    }

    [Fact]
    public async Task ForkFromParentBranch_WithBranchCompactionEnabled_CompactsSubAgentBranch()
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
                    Enabled = true,
                    CompactOnFork = false,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 2 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentBranch = (await store.LoadBranchAsync("parent-session", "main"))!;
        for (var i = 0; i < 4; i++)
        {
            parentBranch.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await store.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedBranch(SubAgentBranchCompaction.Enabled));

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childBranch = await store.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-2", "message-3");
        strategy.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ForkFromParentBranch_WithBranchCompactionDisabled_SkipsGlobalForkCompaction()
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
                    Enabled = true,
                    CompactOnFork = true,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 1 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentBranch = (await store.LoadBranchAsync("parent-session", "main"))!;
        for (var i = 0; i < 3; i++)
        {
            parentBranch.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await store.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedBranch(SubAgentBranchCompaction.Disabled));

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childBranch = await store.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
        strategy.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ForkFromParentBranch_WithBranchCompactionPreferCache_UsesMatchingCopiedCompactionState()
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
                    Enabled = true,
                    CompactOnFork = false,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 1 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentBranch = (await store.LoadBranchAsync("parent-session", "main"))!;
        for (var i = 0; i < 4; i++)
        {
            parentBranch.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }

        var cachedResult = CompactionResult.FromOriginalAndCompacted(
            parentBranch.Messages,
            parentBranch.Messages.TakeLast(2).ToList(),
            new MessageCountingCompactionOptions { TargetMessageCount = 2 });
        new MiddlewareState()
            .WithCompaction(new CompactionStateData().WithCompaction(CompactionSnapshot.FromResult(cachedResult)))
            .SaveToBranch(parentBranch, agent.StateFactories);
        await store.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedBranch(SubAgentBranchCompaction.PreferCache));

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childBranch = await store.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-2", "message-3");
        strategy.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ForkFromParentBranch_WithBranchCompactionPreferCache_FallsBackWhenCacheDoesNotMatch()
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
                    Enabled = true,
                    CompactOnFork = false,
                    Strategy = new MessageCountingCompactionOptions { TargetMessageCount = 1 }
                }
            });
        await agent.CreateSessionAsync("parent-session");

        var parentBranch = (await store.LoadBranchAsync("parent-session", "main"))!;
        for (var i = 0; i < 3; i++)
        {
            parentBranch.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }

        var cachedResult = CompactionResult.FromOriginalAndCompacted(
            parentBranch.Messages.Take(2).ToList(),
            parentBranch.Messages.Take(1).ToList(),
            new MessageCountingCompactionOptions { TargetMessageCount = 1 });
        new MiddlewareState()
            .WithCompaction(new CompactionStateData().WithCompaction(CompactionSnapshot.FromResult(cachedResult)))
            .SaveToBranch(parentBranch, agent.StateFactories);
        await store.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(store, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedBranch(SubAgentBranchCompaction.PreferCache));

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childBranch = await store.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-2");
        strategy.CallCount.Should().Be(1);
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
        string branchId)
    {
        var function = AIFunctionFactory.Create(
            (string query) => query,
            new AIFunctionFactoryOptions { Name = "call_subagent", Description = "Calls a subagent." });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "ParentAgent");
        var session = (await store.LoadSessionAsync(sessionId))!;
        session.Store = store;
        var branch = (await store.LoadBranchAsync(sessionId, branchId))!;
        var agentContext = new AgentContext(
            "ParentAgent",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            branch,
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
