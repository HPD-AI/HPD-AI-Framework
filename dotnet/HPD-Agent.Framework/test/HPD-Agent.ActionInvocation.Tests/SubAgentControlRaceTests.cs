using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentControlRaceTests
{
    [Fact]
    public async Task WaitBaselineExecutionIsNotSatisfiedByLaterExecution()
    {
        var inner = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var store = new ObservationBarrierSessionStore(inner);
        var (parent, context) = await CreateParentAsync(store);
        var child = await RegisterChildAsync(store, parent, "worker-1", "child-1");
        await store.AppendThreadEventsAsync(
            child,
            [new ThreadExecutionStartedEvent("execution-a", "worker-agent", DateTimeOffset.UtcNow)]);

        store.ObserveRoute = child;
        using var waitJson = JsonDocument.Parse(
            """{"children":["worker-1"],"mode":"any","timeoutSeconds":30}""");
        var waitTask = SubAgentRuntime.ControlAsync(
            "wait", waitJson.RootElement, context, CancellationToken.None);

        await store.ObservationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.AppendThreadEventsAsync(
            child,
            [
                new ThreadExecutionFinishedEvent(
                    "execution-a", "worker-agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow),
                new ThreadExecutionStartedEvent("execution-b", "worker-agent", DateTimeOffset.UtcNow)
            ]);

        var waited = Assert.IsType<SubAgentWaitResult>(await waitTask);
        var result = Assert.Single(waited.Children);
        Assert.False(waited.TimedOut);
        Assert.Equal("worker-1", result.Child);
        Assert.Equal("execution-a", result.ThreadExecutionId);
        Assert.Equal(ThreadExecutionStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task CancelReportsNaturalCompletionRaceInsteadOfClaimingCancellation()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var (parent, context) = await CreateParentAsync(store);
        var child = await RegisterChildAsync(store, parent, "worker-1", "child-1");
        await using var agent = new Agent(
            new AgentConfig
            {
                Name = "worker-agent",
                SessionStore = store,
                EventComposition = CoreAgentEventComposition.Instance
            },
            baseClient: null,
            mergedOptions: null);
        var controller = ThreadExecutionControllerRegistry.For(store);
        var execution = await controller.TryAcquireAsync(
            new ThreadExecutionStartRequest(child, "execution-natural", agent));
        Assert.True(execution.Acquired);

        using var cancelJson = JsonDocument.Parse(
            """{"child":"worker-1","reason":"no longer needed"}""");
        var cancelled = Assert.IsType<SubAgentOperationResult>(await SubAgentRuntime.ControlAsync(
            "cancel", cancelJson.RootElement, context, CancellationToken.None));

        Assert.Equal(SubAgentOperationStatus.Unavailable, cancelled.Status);
        Assert.Equal("execution-natural", cancelled.ThreadExecutionId);
        Assert.Equal("subagent_cancel_raced", cancelled.Error?.Code);
        Assert.Equal(AgentInputDisposition.NoActiveExecution.ToString(), cancelled.Error?.Message);

        await controller.ReleaseAsync(
            execution.Lease!,
            new ThreadExecutionTerminalResult(ThreadExecutionOutcome.Succeeded));
    }

    private static async Task<(ThreadKey Parent, FunctionExecutionContext Context)> CreateParentAsync(
        ISessionStore store)
    {
        var session = new Session("session");
        await store.SaveSessionAsync(session);
        var thread = session.CreateThread("parent-agent", "parent");
        await store.SaveInitialThreadAsync(session.Id, thread);
        thread.Session = session;
        session.Store = store;
        var function = AIFunctionFactory.Create(
            (string input) => input,
            new AIFunctionFactoryOptions { Name = "SubAgents" });
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "parent-agent");
        var agentContext = new AgentContext(
            "parent-agent",
            "conversation",
            state,
            new HPD.Events.Core.EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var before = agentContext.AsBeforeFunction(
            function,
            "tool-call",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);
        return (new ThreadKey(session.Id, thread.Id), new FunctionExecutionContext(
            before,
            new FunctionRequest
            {
                Function = function,
                CallId = "tool-call",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            }));
    }

    private static async Task<ThreadKey> RegisterChildAsync(
        ISessionStore store,
        ThreadKey parent,
        string localId,
        string threadId)
    {
        var route = new ThreadKey(parent.SessionId, threadId);
        await store.AppendThreadEventsAsync(
            route,
            [new ThreadCreatedEvent(
                "worker-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                parent.SessionId, parent.ThreadId, "worker",
                ParentToolCallId: $"call-{localId}")
            {
                SessionId = route.SessionId,
                ThreadId = route.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await new SubAgentChildRegistry(store).RegisterAsync(parent, new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId(localId),
            RoleName = "worker",
            CapabilityId = CapabilityId.Create($"test:{localId}"),
            ChildAgentId = "worker-agent",
            ChildThread = route,
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = $"create-{localId}",
            ParentToolCallId = $"call-{localId}",
            ExecutionPolicy = SubAgentTestPolicies.Default,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return route;
    }

    private sealed class ObservationBarrierSessionStore(ISessionStore inner) : ISessionStore
    {
        public AgentEventCodec EventCodec => inner.EventCodec;
        public ThreadKey? ObserveRoute { get; set; }
        public TaskCompletionSource ObservationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Session?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            inner.LoadSessionAsync(sessionId, cancellationToken);
        public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default) =>
            inner.SaveSessionAsync(session, cancellationToken);
        public ValueTask<SessionPreparationResult> TryPrepareSessionAsync(
            Session session, CancellationToken cancellationToken = default) =>
            inner.TryPrepareSessionAsync(session, cancellationToken);
        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default) =>
            inner.ListSessionIdsAsync(cancellationToken);
        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            inner.DeleteSessionAsync(sessionId, cancellationToken);
        public ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(
            ThreadKey thread, IReadOnlyList<AgentEvent> events, ThreadAppendCondition condition = default,
            CancellationToken cancellationToken = default) =>
            inner.AppendThreadEventsAsync(thread, events, condition, cancellationToken);
        public ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(
            ThreadKey thread, IReadOnlyList<AgentEvent> events, ThreadJournalCursor expectedCursor,
            CancellationToken cancellationToken = default) =>
            inner.ReplaceThreadEventsAsync(thread, events, expectedCursor, cancellationToken);
        public ValueTask<ThreadDescriptor?> GetThreadAsync(
            ThreadKey thread, CancellationToken cancellationToken = default) =>
            inner.GetThreadAsync(thread, cancellationToken);
        public IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(
            string sessionId, ThreadListRequest request, CancellationToken cancellationToken = default) =>
            inner.ListThreadsAsync(sessionId, request, cancellationToken);
        public ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(
            ThreadKey thread, CancellationToken cancellationToken = default) =>
            inner.GetThreadEventHeadAsync(thread, cancellationToken);
        public IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(
            ThreadKey thread, ThreadEventReadRequest request, CancellationToken cancellationToken = default) =>
            inner.ReadThreadEventsAsync(thread, request, cancellationToken);

        public async IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(
            ThreadKey thread,
            ThreadJournalCursor after,
            ThreadObservationOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (thread == ObserveRoute)
                ObservationStarted.TrySetResult();
            await foreach (var batch in inner.ObserveThreadEventsAsync(thread, after, options, cancellationToken))
                yield return batch;
        }

        public Task DeleteThreadAsync(
            string sessionId, string threadId, CancellationToken cancellationToken = default) =>
            inner.DeleteThreadAsync(sessionId, threadId, cancellationToken);
        public Task<int> DeleteInactiveSessionsAsync(
            TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default) =>
            inner.DeleteInactiveSessionsAsync(inactivityThreshold, dryRun, cancellationToken);
    }
}
