using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentCreationCrashRecoveryTests
{
    [Fact]
    public async Task IsolatedCreationMaterializesAndReplaysTheReservedExactRoute()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var parent = new ThreadKey("session", "parent");
        await CreateParentAsync(store, parent);
        var resolver = new FixedResolver(store);
        var context = await CreateContextAsync(store, parent, resolver);
        var capability = CapabilityId.Create("test:worker");
        var definition = CreateDefinition(SubAgentContextPolicy.Isolated);
        var request = new SubAgentRuntime.SubAgentInvocationRequest
        {
            Definition = definition,
            Input = "work",
            CapabilityId = capability,
            ParentContext = context
        };

        var first = await SubAgentRuntime.InvokeAsync(request, CancellationToken.None);
        var replay = await SubAgentRuntime.InvokeAsync(request, CancellationToken.None);

        Assert.StartsWith("background_unavailable:", first.Text, StringComparison.Ordinal);
        Assert.StartsWith("background_unavailable:", replay.Text, StringComparison.Ordinal);
        var key = new SubAgentCreationKey(parent, "tool-call", capability);
        var creation = Assert.IsType<SubAgentCreationRecord>(
            await new JournalSubAgentCreationStore(store).GetSubAgentCreationAsync(key));
        Assert.Equal(SubAgentCreationPhase.Registered, creation.Phase);
        Assert.NotNull(await store.LoadSessionAsync(creation.ChildThread.SessionId));
        var child = Assert.IsType<ThreadDescriptor>(await store.GetThreadAsync(creation.ChildThread));
        Assert.Equal(creation.ChildThread, child.Key);
        Assert.Equal(parent.SessionId, child.RuntimeChild!.ParentSessionId);
        Assert.Equal(parent.ThreadId, child.RuntimeChild.ParentThreadId);
        Assert.Equal(creation.InvocationId, child.RuntimeChild.InvocationId);
        Assert.Equal("Isolated", child.RuntimeChild.ContextPolicy);
        Assert.Single((await new SubAgentChildRegistry(store).ProjectAsync(parent)).AvailableChildren);
    }

    [Fact]
    public async Task RestartFromChildCreatedReportsMissingReservedRouteDistinctly()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var parent = new ThreadKey("session", "parent");
        await CreateParentAsync(store, parent);
        var key = new SubAgentCreationKey(parent, "tool-call", CapabilityId.Create("test:worker"));
        var creationStore = new JournalSubAgentCreationStore(store);
        var reserved = await creationStore.TryReserveSubAgentCreationAsync(key, new SubAgentCreationRequest
        {
            RoleName = "worker",
            ChildAgentId = "worker-agent",
            Context = SubAgentCreationContext.Fresh,
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            InputFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("work")))
        });
        await creationStore.WriteSubAgentCreationAsync(
            reserved.Record with { Phase = SubAgentCreationPhase.ChildCreated, Revision = 2 },
            new SubAgentCreationWriteCondition(1));
        var context = await CreateContextAsync(store, parent, new FixedResolver(store));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SubAgentRuntime.InvokeAsync(new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = CreateDefinition(),
                Input = "work",
                CapabilityId = key.CapabilityId,
                ParentContext = context
            }, CancellationToken.None));

        Assert.Equal("subagent_reserved_route_missing", exception.Message);
        Assert.Empty((await new SubAgentChildRegistry(store).ProjectAsync(parent)).AvailableChildren);
    }

    [Fact]
    public async Task RestartFromChildCreatedRejectsTamperedExactRouteWithoutRegistering()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var parent = new ThreadKey("session", "parent");
        await CreateParentAsync(store, parent);
        var key = new SubAgentCreationKey(parent, "tool-call", CapabilityId.Create("test:worker"));
        var creationStore = new JournalSubAgentCreationStore(store);
        var reserved = await creationStore.TryReserveSubAgentCreationAsync(key, new SubAgentCreationRequest
        {
            RoleName = "worker",
            ChildAgentId = "worker-agent",
            Context = SubAgentCreationContext.Fresh,
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            InputFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("work")))
        });
        await store.AppendThreadEventsAsync(
            reserved.Record.ChildThread,
            [new ThreadCreatedEvent(
                "worker-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                parent.SessionId, "tampered-parent", "worker",
                InvocationId: reserved.Record.InvocationId,
                ParentToolCallId: key.ParentToolCallId,
                ContextPolicy: "Fresh")
            {
                SessionId = reserved.Record.ChildThread.SessionId,
                ThreadId = reserved.Record.ChildThread.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await creationStore.WriteSubAgentCreationAsync(
            reserved.Record with { Phase = SubAgentCreationPhase.ChildCreated, Revision = 2 },
            new SubAgentCreationWriteCondition(1));
        var context = await CreateContextAsync(store, parent, new FixedResolver(store));
        var definition = CreateDefinition();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SubAgentRuntime.InvokeAsync(new SubAgentRuntime.SubAgentInvocationRequest
            {
                Definition = definition,
                Input = "work",
                CapabilityId = key.CapabilityId,
                ParentContext = context
            }, CancellationToken.None));

        Assert.Equal("subagent_exact_route_collision", exception.Message);
        Assert.Empty((await new SubAgentChildRegistry(store).ProjectAsync(parent)).AvailableChildren);
        Assert.Equal(
            SubAgentCreationPhase.ChildCreated,
            (await creationStore.GetSubAgentCreationAsync(key))!.Phase);
    }

    [Fact]
    public async Task RestartFromChildCreatedRegistersExactRouteWithoutDuplicate()
    {
        var first = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var parent = new ThreadKey("session", "parent");
        await CreateParentAsync(first, parent);
        var key = new SubAgentCreationKey(parent, "tool-call", CapabilityId.Create("test:worker"));
        var creationStore = new JournalSubAgentCreationStore(first);
        var reserved = await creationStore.TryReserveSubAgentCreationAsync(key, new SubAgentCreationRequest
        {
            RoleName = "worker",
            ChildAgentId = "worker-agent",
            Context = SubAgentCreationContext.Fresh,
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            InputFingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("work")))
        });
        await AppendExactChildAsync(first, reserved.Record);
        var childCreated = reserved.Record with
        {
            Phase = SubAgentCreationPhase.ChildCreated,
            Revision = 2
        };
        await creationStore.WriteSubAgentCreationAsync(
            childCreated, new SubAgentCreationWriteCondition(1));

        var restart = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        await restart.SaveSessionAsync(new Session(parent.SessionId));
        await restart.AppendThreadEventsAsync(
            parent,
            (await ReadAllAsync(first, parent)).Select(ResetPosition).ToArray(),
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await restart.AppendThreadEventsAsync(
            childCreated.ChildThread,
            (await ReadAllAsync(first, childCreated.ChildThread)).Select(ResetPosition).ToArray(),
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        var context = await CreateContextAsync(restart, parent, new FixedResolver(restart));
        var definition = CreateDefinition();

        var result = await SubAgentRuntime.InvokeAsync(new SubAgentRuntime.SubAgentInvocationRequest
        {
            Definition = definition,
            Input = "work",
            CapabilityId = key.CapabilityId,
            ParentContext = context
        }, CancellationToken.None);

        Assert.StartsWith("background_unavailable:", result.Text, StringComparison.Ordinal);
        var projection = await new SubAgentChildRegistry(restart).ProjectAsync(parent);
        var registered = Assert.Single(projection.AvailableChildren).Value;
        Assert.Equal(childCreated.LocalId, registered.LocalId);
        Assert.Equal(childCreated.ChildThread, registered.ChildThread);
        var latest = Assert.IsType<SubAgentCreationRecord>(
            await new JournalSubAgentCreationStore(restart).GetSubAgentCreationAsync(key));
        Assert.Equal(SubAgentCreationPhase.Registered, latest.Phase);
        var children = new List<ThreadDescriptor>();
        await foreach (var descriptor in restart.ListThreadsAsync(
                           parent.SessionId, new ThreadListRequest { IncludeHidden = true }))
            if (descriptor.Kind == ThreadKind.SubAgent) children.Add(descriptor);
        Assert.Single(children);
        Assert.Equal(childCreated.ChildThread, children[0].Key);
    }

    private static async Task CreateParentAsync(ISessionStore store, ThreadKey parent)
    {
        var session = new Session(parent.SessionId);
        await store.SaveSessionAsync(session);
        await store.AppendThreadEventsAsync(parent,
            [new ThreadCreatedEvent("parent-agent", null, null, null, null, DateTime.UtcNow)
            {
                SessionId = parent.SessionId,
                ThreadId = parent.ThreadId
            }], new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
    }

    private static SubAgent CreateDefinition(
        SubAgentContextPolicy contextPolicy = SubAgentContextPolicy.Fresh) => SubAgent.FromConfig(
        "worker-agent", "worker", "Does work.", new AgentConfig(),
        contextPolicy, metadata: null,
        AgentInvocationModePolicy.BackgroundOnly, operationNotification: null);

    private static async Task AppendExactChildAsync(ISessionStore store, SubAgentCreationRecord creation) =>
        _ = await store.AppendThreadEventsAsync(
            creation.ChildThread,
            [new ThreadCreatedEvent(
                creation.Request.ChildAgentId, null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                creation.Key.Parent.SessionId, creation.Key.Parent.ThreadId, creation.Request.RoleName,
                InvocationId: creation.InvocationId,
                ParentToolCallId: creation.Key.ParentToolCallId,
                ContextPolicy: "Fresh")
            {
                SessionId = creation.ChildThread.SessionId,
                ThreadId = creation.ChildThread.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));

    private static async Task<FunctionExecutionContext> CreateContextAsync(
        InMemorySessionStore store, ThreadKey parent, IAgentRuntimeResolver resolver)
    {
        var session = Assert.IsType<Session>(await store.LoadSessionAsync(parent.SessionId));
        session.Store = store;
        var thread = Assert.IsType<Thread>(await store.ProjectThreadAsync(
            parent.SessionId, parent.ThreadId, ThreadProjectionPurpose.ThreadHistory));
        thread.Session = session;
        var services = new ServiceCollection().AddSingleton(resolver).BuildServiceProvider();
        var function = AIFunctionFactory.Create((string value) => value,
            new AIFunctionFactoryOptions { Name = "SubAgents" });
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "parent-agent");
        var agentContext = new AgentContext(
            "parent-agent", "conversation", state, new HPD.Events.Core.EventCoordinator(),
            session, thread, CancellationToken.None, services: services);
        var before = agentContext.AsBeforeFunction(
            function, "tool-call", new Dictionary<string, object?>(), new AgentRunConfig(), null, null);
        return new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = "tool-call",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = agentContext.EventCoordinator
        });
    }

    private sealed class FixedResolver : IAgentRuntimeResolver
    {
        private readonly Agent _agent;
        public FixedResolver(ISessionStore store) => _agent = new Agent(new AgentConfig
        {
            Name = "worker-agent",
            SessionStore = store,
            EventComposition = CoreAgentEventComposition.Instance
        }, baseClient: null, mergedOptions: null);
        public Task<IAgentRuntimeLease> GetOrBuildAsync(
            string agentId, string sessionId, string threadId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IAgentRuntimeLease>(new Lease(_agent));
        private sealed class Lease(Agent agent) : IAgentRuntimeLease
        {
            public Agent Agent { get; } = agent;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async Task<IReadOnlyList<AgentEvent>> ReadAllAsync(ISessionStore store, ThreadKey key)
    {
        var head = Assert.IsType<ThreadEventHead>(await store.GetThreadEventHeadAsync(key));
        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
                           key, new ThreadEventReadRequest(
                               ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber)))
            events.AddRange(batch.Events);
        return events;
    }

    private static AgentEvent ResetPosition(AgentEvent evt) => evt with { ThreadSequenceNumber = 0 };
}
