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
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var agent = await BuildAgentAsync(repository);
        var session = new HPD.Agent.Session("parent-session");
        var parentBranch = session.CreateBranch("main");
        parentBranch.AddMessage(new ChatMessage(ChatRole.User, "Parent context"));
        parentBranch.AddMessage(new ChatMessage(ChatRole.Assistant, "Parent answer"));
        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(repository, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            executionPolicy: null,
            metadata: new Dictionary<string, object> { ["purpose"] = "review-current-branch" });

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.BranchId.Should().StartWith("subagent/reviewer/");

        var childBranch = await repository.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch.Should().NotBeNull();
        childBranch!.Messages.Should().HaveCount(parentBranch.Messages.Count);
        MetadataString(childBranch, "kind").Should().Be("subagent");
        MetadataString(childBranch, "subAgentName").Should().Be("Reviewer");
        MetadataString(childBranch, "parentSessionId").Should().Be("parent-session");
        MetadataString(childBranch, "parentBranchId").Should().Be("main");
        MetadataString(childBranch, "sessionPolicy").Should().Be(nameof(SubAgentSessionPolicy.ParentSession));
        MetadataString(childBranch, "branchPolicy").Should().Be(nameof(SubAgentBranchPolicy.ForkFromParentBranch));
        MetadataString(childBranch, "visibility").Should().Be("hidden");
        MetadataString(childBranch, "purpose").Should().Be("review-current-branch");

        context.ResultMetadata.TryGet<string>("subAgentStatus", out var status).Should().BeTrue();
        status.Should().Be("started");
        context.ResultMetadata.TryGet<string>("subAgentBranchId", out var resultBranchId).Should().BeTrue();
        resultBranchId.Should().Be(route.BranchId);
    }

    [Fact]
    public async Task FreshBranch_CreatesEmptyBranchInParentSession()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var agent = await BuildAgentAsync(repository);
        var session = new HPD.Agent.Session("parent-session");
        var parentBranch = session.CreateBranch("main");
        parentBranch.AddMessage(new ChatMessage(ChatRole.User, "This should not be copied"));
        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(repository, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Researcher",
            "Starts without parent conversation history.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionFreshBranch());

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        route.SessionId.Should().Be("parent-session");
        route.BranchId.Should().StartWith("subagent/researcher/");

        var childBranch = await repository.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch.Should().NotBeNull();
        childBranch!.Messages.Should().BeEmpty();
        MetadataString(childBranch, "kind").Should().Be("subagent");
        MetadataString(childBranch, "branchPolicy").Should().Be(nameof(SubAgentBranchPolicy.FreshBranch));
    }

    [Fact]
    public async Task ForkFromParentBranch_WithBranchCompactionEnabled_CompactsSubAgentBranch()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 2);
        var agent = await BuildAgentAsync(
            repository,
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
        var session = new HPD.Agent.Session("parent-session");
        var parentBranch = session.CreateBranch("main");
        for (var i = 0; i < 4; i++)
        {
            parentBranch.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(repository, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedBranch(SubAgentBranchCompaction.Enabled));

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childBranch = await repository.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-2", "message-3");
        strategy.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ForkFromParentBranch_WithBranchCompactionDisabled_SkipsGlobalForkCompaction()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var strategy = new RetainLastMessagesCompactionStrategy(retainCount: 1);
        var agent = await BuildAgentAsync(
            repository,
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
        var session = new HPD.Agent.Session("parent-session");
        var parentBranch = session.CreateBranch("main");
        for (var i = 0; i < 3; i++)
        {
            parentBranch.AddMessage(new ChatMessage(ChatRole.User, $"Parent context {i}") { MessageId = $"message-{i}" });
        }
        await repository.SaveSessionAsync(session);
        await repository.SaveInitialBranchAsync("parent-session", parentBranch);

        var context = await CreateFunctionContextAsync(repository, "parent-session", "main");
        var subAgent = SubAgent.FromConfig(
            "Reviewer",
            "Reviews the current branch.",
            MinimalConfig(),
            SubAgentExecutionPolicies.ParentSessionForkedBranch(SubAgentBranchCompaction.Disabled));

        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, context, CancellationToken.None);

        var childBranch = await repository.LoadBranchAsync(route.SessionId, route.BranchId);
        childBranch!.Messages.Select(message => message.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
        strategy.CallCount.Should().Be(0);
    }

    private static async Task<Agent> BuildAgentAsync(
        ISessionRepository repository,
        params IAgentMiddleware[] middlewares)
    {
        var builder = new AgentBuilder(MinimalConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionRepository(repository);

        foreach (var middleware in middlewares)
            builder.WithMiddleware(middleware);

        return await builder.BuildAsync(CancellationToken.None);
    }

    private static string MetadataString(Branch branch, string key) =>
        branch.Metadata[key] switch
        {
            string value => value,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } value => value.GetString()!,
            object value => value.ToString()!,
            null => string.Empty
        };

    private static async Task<FunctionExecutionContext> CreateFunctionContextAsync(
        ISessionRepository repository,
        string sessionId,
        string branchId)
    {
        var function = AIFunctionFactory.Create(
            (string query) => query,
            new AIFunctionFactoryOptions { Name = "call_subagent", Description = "Calls a subagent." });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "ParentAgent");
        var session = (await repository.LoadSessionAsync(sessionId))!;
        var branch = (await repository.LoadBranchAsync(sessionId, branchId))!;
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
