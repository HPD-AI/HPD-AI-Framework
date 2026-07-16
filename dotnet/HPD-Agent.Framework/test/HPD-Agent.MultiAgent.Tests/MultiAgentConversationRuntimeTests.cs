using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPD.Graph.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent.Tests;

public class MultiAgentConversationRuntimeTests
{
    [Fact]
    public async Task SharedWorkflowThread_RoutesAllNodesToSameThread()
    {
        var store = new InMemorySessionStore();
        var runtime = new MultiAgentConversationRuntime(
            MultiAgentConversationPolicies.SharedWorkflowThread("workflow-session"),
            store,
            "TestWorkflow",
            "exec-1",
            "Solve this.");
        var context = await CreateContextAsync(store);

        var first = await runtime.ResolveRouteAsync(CreateRouteContext(context, "researcher"), CancellationToken.None);
        var second = await runtime.ResolveRouteAsync(CreateRouteContext(context, "reviewer"), CancellationToken.None);

        first.SessionId.Should().Be("workflow-session");
        second.SessionId.Should().Be("workflow-session");
        first.ThreadId.Should().Be("workflow");
        second.ThreadId.Should().Be("workflow");

        var thread = await store.ProjectThreadAsync("workflow-session", "workflow", ThreadProjectionPurpose.ThreadHistory);
        thread.Should().NotBeNull();
        thread!.Metadata["conversationMode"].Should().Be(nameof(MultiAgentConversationMode.SharedWorkflowThread));

        var session = await store.LoadSessionAsync("workflow-session");
        session.Should().NotBeNull();
        session!.Metadata["workspaceKind"].Should().Be("multi-agent-workflow");
        session.Metadata["conversationMode"].Should().Be(nameof(MultiAgentConversationMode.SharedWorkflowThread));
    }

    [Fact]
    public async Task ThreadPerAgent_CreatesStableThreadForEachNode()
    {
        var store = new InMemorySessionStore();
        var runtime = new MultiAgentConversationRuntime(
            MultiAgentConversationPolicies.ThreadPerAgent("workflow-session", "node"),
            store,
            "TestWorkflow",
            "exec-1",
            "Solve this.");
        var context = await CreateContextAsync(store);

        var researcher = await runtime.ResolveRouteAsync(CreateRouteContext(context, "researcher"), CancellationToken.None);
        var reviewer = await runtime.ResolveRouteAsync(CreateRouteContext(context, "reviewer"), CancellationToken.None);
        var researcherAgain = await runtime.ResolveRouteAsync(CreateRouteContext(context, "researcher"), CancellationToken.None);

        researcher.SessionId.Should().Be("workflow-session");
        researcher.ThreadId.Should().Be("node-exec-1-researcher");
        reviewer.ThreadId.Should().Be("node-exec-1-reviewer");
        researcherAgain.ThreadId.Should().Be(researcher.ThreadId);

        var thread = await store.ProjectThreadAsync("workflow-session", researcher.ThreadId!, ThreadProjectionPurpose.ThreadHistory);
        thread.Should().NotBeNull();
        thread!.Metadata["nodeId"].Should().Be("researcher");
    }

    [Fact]
    public async Task ForkThreadPerAgent_ForksEachNodeFromRootInputThread()
    {
        var store = new InMemorySessionStore();
        var runtime = new MultiAgentConversationRuntime(
            MultiAgentConversationPolicies.ForkThreadPerAgent("workflow-session", "root", "node"),
            store,
            "TestWorkflow",
            "exec-1",
            "Solve this.");
        var context = await CreateContextAsync(store);

        var researcher = await runtime.ResolveRouteAsync(CreateRouteContext(context, "researcher"), CancellationToken.None);
        var reviewer = await runtime.ResolveRouteAsync(CreateRouteContext(context, "reviewer"), CancellationToken.None);

        researcher.ThreadId.Should().Be("node-exec-1-researcher");
        reviewer.ThreadId.Should().Be("node-exec-1-reviewer");

        var root = await store.ProjectThreadAsync("workflow-session", "root", ThreadProjectionPurpose.ThreadHistory);
        root.Should().NotBeNull();
        root!.Messages.Should().ContainSingle(message => message.Text == "Solve this.");

        var researcherThread = await store.ProjectThreadAsync("workflow-session", researcher.ThreadId!, ThreadProjectionPurpose.ThreadHistory);
        researcherThread.Should().NotBeNull();
        researcherThread!.ForkedFrom.Should().Be("root");
        researcherThread.Messages.Should().ContainSingle(message => message.Text == "Solve this.");
        researcherThread.Metadata["nodeId"].Should().Be("researcher");
    }

    [Fact]
    public async Task BuildAsync_ConversationPolicyRequiresWorkflowSessionStore()
    {
        var workflow = AgentWorkflow.Create()
            .WithConversation(MultiAgentConversationPolicies.SharedWorkflowThread())
            .AddAgent("agent", MinimalConfig());

        var act = () => workflow.BuildAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*session store is required*");
    }

    [Fact]
    public async Task ExistingSession_IsEnrichedWithWorkflowMetadata()
    {
        var store = new InMemorySessionStore();
        var agent = await new AgentBuilder(MinimalConfig())
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);
        await agent.CreateSessionAsync(
            "existing-workspace-session",
            new Dictionary<string, object> { ["owner"] = "user" },
            CancellationToken.None);

        var runtime = new MultiAgentConversationRuntime(
            MultiAgentConversationPolicies.ThreadPerAgent("existing-workspace-session"),
            store,
            "WorkspaceWorkflow",
            "exec-1",
            "Solve this.");
        var context = await CreateContextAsync(store);

        _ = await runtime.ResolveRouteAsync(CreateRouteContext(context, "researcher"), CancellationToken.None);

        var session = await store.LoadSessionAsync("existing-workspace-session");
        session.Should().NotBeNull();
        session!.Metadata["owner"].Should().Be("user");
        session.Metadata["workspaceKind"].Should().Be("multi-agent-workflow");
        session.Metadata["workflowName"].Should().Be("WorkspaceWorkflow");
        session.Metadata["conversationMode"].Should().Be(nameof(MultiAgentConversationMode.ThreadPerAgent));
    }

    private static async Task<AgentGraphContext> CreateContextAsync(ISessionStore store)
    {
        var agent = await new AgentBuilder(MinimalConfig())
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);
        var graph = new GraphBuilder()
            .WithName("TestWorkflow")
            .AddStartNode()
            .AddEndNode()
            .Build();

        return new AgentGraphContext(
            "exec-1",
            graph,
            new ServiceCollection().BuildServiceProvider(),
            new Dictionary<string, HPD.Agent.Agent> { ["agent"] = agent },
            new Dictionary<string, AgentNodeOptions> { ["agent"] = new() },
            originalInput: "Solve this.",
            workflowName: "TestWorkflow");
    }

    private static MultiAgentConversationContext CreateRouteContext(
        AgentGraphContext context,
        string nodeId)
    {
        var agent = context.GetAgent("agent")!;
        return new MultiAgentConversationContext(
            context.ExecutionId,
            context.WorkflowName,
            nodeId,
            agent,
            "Solve this.",
            context,
            new AgentNodeOptions());
    }

    private static AgentConfig MinimalConfig() => new()
    {
        Name = "TestAgent",
        SystemInstructions = "Test agent."
    };
}
