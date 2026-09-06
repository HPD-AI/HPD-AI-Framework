using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentContinuationConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSameKeyContinueHasOneOwnerAndRunningFollower()
    {
        var fixture = await CreateFixtureAsync();
        using var json = JsonDocument.Parse("""{"child":"worker-1","input":"continue work"}""");

        var ownerTask = SubAgentRuntime.ControlAsync(
            "continue", json.RootElement, fixture.Context, CancellationToken.None);
        var enteredOrOwner = await Task.WhenAny(
            fixture.Resolver.Entered.Task,
            ownerTask,
            Task.Delay(TimeSpan.FromSeconds(5)));
        if (enteredOrOwner == ownerTask)
            _ = await ownerTask;
        Assert.Same(fixture.Resolver.Entered.Task, enteredOrOwner);
        var follower = Assert.IsType<SubAgentOperationResult>(await SubAgentRuntime.ControlAsync(
            "continue", json.RootElement, fixture.Context, CancellationToken.None));

        Assert.Equal(SubAgentOperationStatus.Running, follower.Status);
        Assert.Equal(1, fixture.Resolver.LeaseCount);
        var admittedEvents = await ReadEventsAsync(fixture.Store, fixture.Child);
        var started = Assert.Single(admittedEvents.OfType<ThreadExecutionStartedEvent>());
        Assert.Equal(follower.ThreadExecutionId, started.ThreadExecutionId);
        fixture.Resolver.Release.TrySetResult();
        var failure = Assert.IsType<SubAgentOperationResult>(await ownerTask);
        Assert.Equal(SubAgentOperationStatus.Failed, failure.Status);
        Assert.Equal("resolver_release", failure.Error?.Message);
        Assert.Equal(1, fixture.Resolver.LeaseCount);
    }

    [Fact]
    public async Task RestartAfterDurableAdmissionReturnsReconciliationWithoutLaunching()
    {
        var fixture = await CreateFixtureAsync();
        const string input = "continue work";
        var executionId = ContinueExecutionId(
            fixture.Parent.SessionId,
            fixture.Parent.ThreadId,
            "tool-call",
            "worker-1",
            input);
        await fixture.Store.AppendThreadEventsAsync(
            fixture.Child,
            [new ThreadExecutionStartedEvent(executionId, "worker-agent", DateTimeOffset.UtcNow)]);
        using var json = JsonDocument.Parse($$"""{"child":"worker-1","input":"{{input}}"}""");

        var result = Assert.IsType<SubAgentOperationResult>(await SubAgentRuntime.ControlAsync(
            "continue", json.RootElement, fixture.Context, CancellationToken.None));

        Assert.Equal(SubAgentOperationStatus.Failed, result.Status);
        Assert.Equal("subagent_reconciliation_required", result.Error?.Code);
        Assert.Equal(executionId, result.ThreadExecutionId);
        Assert.Equal(0, fixture.Resolver.LeaseCount);
        Assert.False((await ThreadExecutionControllerRegistry.For(fixture.Store)
            .FindActiveAsync(fixture.Child)).IsActive);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var session = new Session("session");
        await store.SaveSessionAsync(session);
        var parentThread = session.CreateThread("parent-agent", "parent");
        await store.SaveInitialThreadAsync(session.Id, parentThread);
        parentThread.Session = session;
        session.Store = store;
        var parent = new ThreadKey(session.Id, parentThread.Id);
        var child = new ThreadKey(session.Id, "child");
        await store.AppendThreadEventsAsync(
            child,
            [new ThreadCreatedEvent(
                "worker-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                parent.SessionId, parent.ThreadId, "worker",
                ParentToolCallId: "call-worker")
            {
                SessionId = child.SessionId,
                ThreadId = child.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await new SubAgentChildRegistry(store).RegisterAsync(parent, new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId("worker-1"),
            RoleName = "worker",
            CapabilityId = CapabilityId.Create("test:worker"),
            ChildAgentId = "worker-agent",
            ChildThread = child,
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "create-worker",
            ParentToolCallId = "call-worker",
            ExecutionPolicy = SubAgentTestPolicies.Default,
            CreatedAt = DateTimeOffset.UtcNow
        });
        var resolver = new BlockingResolver();
        var services = new ServiceCollection().AddSingleton<IAgentRuntimeResolver>(resolver).BuildServiceProvider();
        var function = AIFunctionFactory.Create(
            (string value) => value,
            new AIFunctionFactoryOptions { Name = "SubAgents" });
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "parent-agent");
        var capabilities = new RuntimeCapabilityRegistry();
        capabilities.Set(new AgentOperationRegistry(new StoreOperationSink(store)));
        var agentContext = new AgentContext(
            "parent-agent", "conversation", state, new HPD.Events.Core.EventCoordinator(),
            session, parentThread, CancellationToken.None, services: services, runtimeCapabilities: capabilities);
        var before = agentContext.AsBeforeFunction(
            function, "tool-call", new Dictionary<string, object?>(), new AgentRunConfig(), null, null);
        var context = new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = "tool-call",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = agentContext.EventCoordinator
        });
        return new Fixture(store, parent, child, resolver, context);
    }

    private static string ContinueExecutionId(
        string sessionId, string threadId, string functionCallId, string child, string input)
    {
        var key = $"{sessionId}|{threadId}|{functionCallId}|{child}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return $"continue-{digest[..24]}";
    }

    private static async Task<IReadOnlyList<AgentEvent>> ReadEventsAsync(
        ISessionStore store,
        ThreadKey route)
    {
        var head = Assert.IsType<ThreadEventHead>(await store.GetThreadEventHeadAsync(route));
        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
                           route,
                           new ThreadEventReadRequest(
                               ThreadJournalCursor.Start(head.Generation),
                               head.ThreadSequenceNumber)))
            events.AddRange(batch.Events);
        return events;
    }

    private sealed class StoreOperationSink(ISessionStore store) : IAgentOperationEventSink
    {
        public async ValueTask AppendAsync(AgentEvent evt, CancellationToken cancellationToken)
            => await store.AppendThreadEventsAsync(new(evt.SessionId!, evt.ThreadId!), [evt], cancellationToken: cancellationToken);
    }

    private sealed class BlockingResolver : IAgentRuntimeResolver
    {
        private int _leaseCount;
        public int LeaseCount => Volatile.Read(ref _leaseCount);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IAgentRuntimeLease> GetOrBuildAsync(
            string agentId, string sessionId, string threadId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _leaseCount);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException("resolver_release");
        }
    }

    private sealed record Fixture(
        InMemorySessionStore Store,
        ThreadKey Parent,
        ThreadKey Child,
        BlockingResolver Resolver,
        FunctionExecutionContext Context);
}
