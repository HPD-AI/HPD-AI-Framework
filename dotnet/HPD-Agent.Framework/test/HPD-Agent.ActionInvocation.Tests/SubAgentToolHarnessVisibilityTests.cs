using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentToolHarnessVisibilityTests
{
    [Fact]
    public async Task InactiveHarnessRoleIsAbsentAndExpansionAddsItNextIteration()
    {
        var core = CreateDescriptor("core", "CoreHarness", requiresActivation: false);
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([core, research]);
        var middleware = new SubAgentAvailabilityMiddleware([declared], toolHarnessActivationEnabled: true);

        var inactive = CreateIterationContext([declared]);
        await middleware.BeforeIterationAsync(inactive, CancellationToken.None);
        var inactiveFunction = Assert.IsAssignableFrom<AIFunction>(Assert.Single(inactive.Options.Tools!));
        Assert.Equal(["core"], GetRoleActions(inactiveFunction));
        Assert.False(GetContract(inactiveFunction).Actions.ContainsKey("researcher"));

        var active = CreateIterationContext([declared]);
        active.UpdateMiddlewareState<ContainerMiddlewareState>(state =>
            state.WithExpandedContainer("ResearchHarness"));
        await middleware.BeforeIterationAsync(active, CancellationToken.None);
        var activeFunction = Assert.IsAssignableFrom<AIFunction>(Assert.Single(active.Options.Tools!));
        Assert.Equal(["core", "researcher"], GetRoleActions(activeFunction).Order());
        Assert.True(GetContract(activeFunction).Actions.ContainsKey("researcher"));
    }

    [Fact]
    public async Task FirstCollapsedIterationDoesNotPermanentlyEraseDeclarationsFromRunPin()
    {
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([research]);
        var middleware = new SubAgentAvailabilityMiddleware([declared], toolHarnessActivationEnabled: true);
        var runConfig = new AgentRunConfig();
        var collapsed = CreateIterationContext([], runConfig: runConfig, runId: "run-1");

        await middleware.BeforeIterationAsync(collapsed, CancellationToken.None);

        Assert.Empty(collapsed.Options.Tools!);
        var activated = CreateIterationContext([declared], runConfig: runConfig, runId: "run-1");
        activated.UpdateMiddlewareState<ContainerMiddlewareState>(state =>
            state.WithExpandedContainer("ResearchHarness"));

        await middleware.BeforeIterationAsync(activated, CancellationToken.None);

        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(activated.Options.Tools!));
        Assert.Equal(["researcher"], GetRoleActions(function));
        Assert.True(GetContract(function).Actions.ContainsKey("researcher"));
    }

    [Fact]
    public async Task NeverCollapseMakesCollapsedHarnessCreationVisibleWithoutExpansion()
    {
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([research]);
        var middleware = new SubAgentAvailabilityMiddleware(
            [declared], toolHarnessActivationEnabled: true, neverCollapse: ["ResearchHarness"]);
        var context = CreateIterationContext([declared]);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(context.Options.Tools!));
        Assert.Equal(["researcher"], GetRoleActions(function));
    }

    [Fact]
    public async Task PublishedHarnessRevisionRemainsPinnedWithinRunAndRefreshesForNextRun()
    {
        var core = CreateDescriptor("core", "CoreHarness", requiresActivation: false);
        var initial = SubAgentsFunctionFactory.Create([core]);
        var middleware = new SubAgentAvailabilityMiddleware([initial], toolHarnessActivationEnabled: true);
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: false);
        var refreshed = SubAgentsFunctionFactory.Create([core, research]);
        var activeRunConfig = new AgentRunConfig();
        var first = CreateIterationContext([initial], runConfig: activeRunConfig, runId: "run-1");
        await middleware.BeforeIterationAsync(first, CancellationToken.None);
        var sameRun = CreateIterationContext([refreshed], runConfig: activeRunConfig, runId: "run-1");
        await middleware.BeforeIterationAsync(sameRun, CancellationToken.None);
        var nextRun = CreateIterationContext([refreshed], runId: "run-2");

        await middleware.BeforeIterationAsync(nextRun, CancellationToken.None);

        Assert.Equal(["core"], GetRoleActions(
            Assert.IsAssignableFrom<AIFunction>(Assert.Single(sameRun.Options.Tools!))));
        Assert.Equal(["core", "researcher"], GetRoleActions(
            Assert.IsAssignableFrom<AIFunction>(Assert.Single(nextRun.Options.Tools!))).Order());
    }

    [Fact]
    public async Task ExistingChildKeepsControlSurfaceWhenCreationHarnessIsInactive()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var session = new Session("session");
        await store.SaveSessionAsync(session);
        var thread = session.CreateThread("parent-agent", "parent");
        await store.SaveInitialThreadAsync(session.Id, thread);
        session.Store = store;
        thread.Session = session;
        var parent = new ThreadKey(session.Id, thread.Id);
        await new SubAgentChildRegistry(store).RegisterAsync(parent, new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId("researcher-1"),
            RoleName = "researcher",
            CapabilityId = CapabilityId.Create("test:researcher"),
            ChildAgentId = "research-agent",
            ChildThread = new ThreadKey(session.Id, "child"),
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "creation",
            ParentToolCallId = "call",
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([research]);
        var middleware = new SubAgentAvailabilityMiddleware([declared], toolHarnessActivationEnabled: true);
        var context = CreateIterationContext([declared], session, thread);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(context.Options.Tools!));
        Assert.Empty(GetRoleActions(function));
        Assert.False(GetContract(function).Actions.ContainsKey("researcher"));
        Assert.All(new[] { "continue", "list", "wait", "sendMessage", "cancel" },
            action => Assert.True(GetContract(function).Actions.ContainsKey(action)));
    }

    [Fact]
    public async Task NoVisibleCreationAndNoExistingChildOmitsSubAgents()
    {
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([research]);
        var middleware = new SubAgentAvailabilityMiddleware([declared], toolHarnessActivationEnabled: true);
        var context = CreateIterationContext([declared]);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        Assert.Empty(context.Options.Tools!);
    }

    [Fact]
    public async Task CollapsedDeclarationStillAuthorizesCapabilityTargetedOverride()
    {
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([research]);
        var middleware = new SubAgentAvailabilityMiddleware([declared], toolHarnessActivationEnabled: true);
        var runConfig = new AgentRunConfig
        {
            SubAgents = new SubAgentRunOverrides
            {
                Capabilities = [new SubAgentRunPolicyOverride { CapabilityId = research.CapabilityId }]
            }
        };
        var context = CreateIterationContext([declared], runConfig: runConfig);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        Assert.Empty(context.Options.Tools!);
    }

    [Fact]
    public async Task UnknownOverrideCapabilityFailsBeforeToolProjection()
    {
        var research = CreateDescriptor("researcher", "ResearchHarness", requiresActivation: true);
        var declared = SubAgentsFunctionFactory.Create([research]);
        var middleware = new SubAgentAvailabilityMiddleware([declared], toolHarnessActivationEnabled: true);
        var runConfig = new AgentRunConfig
        {
            SubAgents = new SubAgentRunOverrides
            {
                Capabilities = [new SubAgentRunPolicyOverride
                {
                    CapabilityId = CapabilityId.Create("test:unknown")
                }]
            }
        };
        var context = CreateIterationContext([declared], runConfig: runConfig);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.BeforeIterationAsync(context, CancellationToken.None));

        Assert.Equal("subagent_override_capability_unknown", exception.Message);
    }

    private static SubAgentActionDescriptor CreateDescriptor(
        string action,
        string harness,
        bool requiresActivation) => new()
    {
        ParentToolHarness = harness,
        RequiresToolHarnessActivation = requiresActivation,
        Action = action,
        Description = $"Creates {action}.",
        CapabilityId = CapabilityId.Create($"test:{harness}:{action}"),
        Definition = SubAgent.FromConfig(
            $"{action}-agent", action, $"Creates {action}.", new AgentConfig(),
            SubAgentContextPolicy.Fresh),
        InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
        InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
        ContextPolicy = SubAgentContextPolicy.Fresh,
        RequiresPermission = true,
        BranchBinder = json => SubAgentGeneratedBranchBinder.Bind(json, allowContext: false)
    };

    private static string[] GetRoleActions(AIFunction function) =>
        Assert.IsAssignableFrom<IReadOnlyList<SubAgentActionDescriptor>>(
                function.AdditionalProperties["SubAgentActions"])
            .Select(action => action.Action)
            .ToArray();

    private static AIFunctionOperationContract GetContract(AIFunction function) =>
        Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function).OperationContract!;

    private static BeforeIterationContext CreateIterationContext(
        IReadOnlyList<AITool> tools,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? runConfig = null,
        string runId = "run")
    {
        session ??= new Session("session");
        thread ??= new Thread("parent", "parent-agent") { Session = session };
        var state = AgentLoopState.InitialSafe([], runId, "conversation", "parent-agent");
        var agentContext = new AgentContext(
            "parent-agent", "conversation", state, new HPD.Events.Core.EventCoordinator(),
            session, thread, CancellationToken.None);
        return agentContext.AsBeforeIteration(
            0, [], new ChatOptions { Tools = tools.ToList() }, runConfig ?? new AgentRunConfig());
    }
}
