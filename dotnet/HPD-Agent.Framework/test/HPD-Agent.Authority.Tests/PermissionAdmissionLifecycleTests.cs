using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Serialization;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public sealed class PermissionAdmissionLifecycleTests
{
    [Fact]
    public async Task SessionApprovalIsResolvedThenCheckpointedAndReusedWithoutAnotherPrompt()
    {
        await using var fixture = await Fixture.Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var inbox = fixture.Events.CreateInbox<PermissionRequestEvent>();
        var admission = fixture.Admit("first", timeout.Token);
        var request = await inbox.Reader.ReadAsync(timeout.Token);
        Assert.Contains(request.Choices, choice => choice.Id == "allow_session");
        Assert.Equal("write", request.FunctionName);
        Assert.Equal(System.Text.Json.JsonValueKind.Object,
            request.Arguments!.Value.ValueKind);
        var response = new PermissionResponseEvent(request.PermissionId, request.SourceName, "allow_session");
        var accepted = await fixture.Events.RespondAsync(request.PermissionId, response,
            async (value, token) => await fixture.Publisher.CommitAndPublishAsync(fixture.Key, (AgentEvent)value, token));
        Assert.True(accepted.Accepted);
        Assert.IsType<Invoke>(await admission);
        Assert.True(PermissionStateCheckpoint.IsPending(fixture.Context));
        Assert.Throws<InvalidOperationException>(() => PermissionStateCheckpoint.EnsureSaved(fixture.Context));
        await PermissionStateCheckpoint.SaveAsync(fixture.Context, UnifiedPermissionModelTests.Factories(), timeout.Token);
        Assert.False(PermissionStateCheckpoint.IsPending(fixture.Context));
        Assert.Contains(PermissionDecisionIndex.StateKey, fixture.Session.MiddlewareState.Keys);
        var second = Assert.IsType<Invoke>(await fixture.Admit("second", timeout.Token));
        Assert.Equal(PermissionGrantSource.StoredDecision, second.Grant!.Source);
        Assert.False(PermissionStateCheckpoint.IsPending(fixture.Context));
        var events = await fixture.Journal();
        var persistedRequest = Assert.Single(events.OfType<PermissionRequestEvent>());
        Assert.Equal("write", persistedRequest.FunctionName);
        Assert.Equal(System.Text.Json.JsonValueKind.Object,
            persistedRequest.Arguments!.Value.ValueKind);
        Assert.Single(events.OfType<PermissionResponseEvent>());
        Assert.Equal(2, events.OfType<PermissionResolvedEvent>().Count());
        Assert.Equal(2, events.OfType<PermissionEvaluatedEvent>().Count());
    }

    [Fact]
    public async Task ReusableApprovalRemainsAvailableBeyondFormerRecordCap()
    {
        await using var fixture = await Fixture.Create();
        var state = new PermissionDecisionStateData();
        for (var index = 0; index < 300; index++)
        {
            var key = new PermissionDecisionKey("coding", $"other/{index}", null, "v1");
            state = PermissionDecisionIndex.Apply(state, new PermissionDecisionRecord
            {
                RecordId = PermissionRecordIdentity.Create(key, PermissionPersistenceKind.Session),
                PermissionId = $"previous-{index}",
                Key = key,
                Persistence = PermissionPersistenceKind.Session,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        fixture.Context.UpdateState(value => value with
        {
            MiddlewareState = value.MiddlewareState.SetState(
                PermissionDecisionIndex.StateKey, state)
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var inbox = fixture.Events.CreateInbox<PermissionRequestEvent>();
        var admission = fixture.Admit("uncapped", timeout.Token);
        var request = await inbox.Reader.ReadAsync(timeout.Token);
        Assert.Contains(request.Choices, choice => choice.Id == "allow_session");
        Assert.Null(request.PersistenceUnavailableReason);
        var accepted = await fixture.Events.RespondAsync(request.PermissionId,
            new PermissionResponseEvent(request.PermissionId, request.SourceName, "allow_session"),
            async (value, token) => await fixture.Publisher.CommitAndPublishAsync(
                fixture.Key, (AgentEvent)value, token));
        Assert.True(accepted.Accepted);
        Assert.IsType<Invoke>(await admission);
        await PermissionStateCheckpoint.SaveAsync(
            fixture.Context, UnifiedPermissionModelTests.Factories(), timeout.Token);
        Assert.Equal(301, fixture.Context.State.MiddlewareState
            .GetState<PermissionDecisionStateData>(PermissionDecisionIndex.StateKey)!
            .Decisions.Length);
    }

    [Fact]
    public async Task MissingStableIdStripsReusableChoicesAndDenialDoesNotCreateState()
    {
        await using var fixture = await Fixture.Create(stableId: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var inbox = fixture.Events.CreateInbox<PermissionRequestEvent>();
        var admission = fixture.Admit("call", timeout.Token);
        var request = await inbox.Reader.ReadAsync(timeout.Token);
        Assert.Equal(PersistenceUnavailableReason.NoStableAgentId, request.PersistenceUnavailableReason);
        Assert.DoesNotContain(request.Choices, choice => choice.Id == "allow_session");
        fixture.Events.Respond(new PermissionResponseEvent(request.PermissionId, request.SourceName, "deny_once"));
        Assert.IsType<Reject>(await admission);
        Assert.False(PermissionStateCheckpoint.IsPending(fixture.Context));
        Assert.Null(fixture.Context.State.MiddlewareState.GetState<PermissionDecisionStateData>(PermissionDecisionIndex.StateKey));
    }

    [Fact]
    public async Task AutoapprovalEvaluatesAndResolvesWithoutRememberingAnything()
    {
        await using var fixture = await Fixture.Create();
        var config = new AgentRunConfig { Security = new() { Approval = AgentApprovalPolicy.AutoApprove } };
        var result = Assert.IsType<Invoke>(await fixture.Admit("automatic", CancellationToken.None, config));
        Assert.Equal(PermissionGrantSource.HostAutoApprove, result.Grant!.Source);
        Assert.Null(result.Grant.ChoiceId);
        Assert.False(PermissionStateCheckpoint.IsPending(fixture.Context));
        var events = await fixture.Journal();
        Assert.Single(events.OfType<PermissionEvaluatedEvent>());
        Assert.Single(events.OfType<PermissionResolvedEvent>());
        Assert.Empty(events.OfType<PermissionRequestEvent>());
    }

    [Fact]
    public async Task RequiredSerializerFailureBlocksCurrentAndLaterUseOfFailedSessionObject()
    {
        await using var fixture = await Fixture.Create();
        fixture.Context.UpdateState(state => state with
        { MiddlewareState = state.MiddlewareState.SetState(PermissionDecisionIndex.StateKey, new PermissionDecisionStateData()) });
        PermissionStateCheckpoint.MarkPending(fixture.Context);
        var factories = UnifiedPermissionModelTests.Factories().ToDictionary(pair => pair.Key, pair => pair.Value);
        factories[PermissionDecisionIndex.StateKey] = factories[PermissionDecisionIndex.StateKey] with
        { Serialize = _ => throw new InvalidOperationException("serializer failed") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => PermissionStateCheckpoint.SaveAsync(fixture.Context, factories, CancellationToken.None));
        Assert.True(PermissionStateCheckpoint.IsPending(fixture.Context));
        Assert.Throws<InvalidOperationException>(() => PermissionStateCheckpoint.EnsureSaved(fixture.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PermissionStateCheckpoint.SaveAsync(fixture.NewContext(),
            UnifiedPermissionModelTests.Factories(), CancellationToken.None));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ExecutionDriverSavesOnceAfterAdmissionBeforeAnyBody(int calls)
    {
        await using var fixture = await Fixture.Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var inbox = fixture.Events.CreateInbox<PermissionRequestEvent>();
        var bodies = 0;
        var function = HPDAIFunctionFactory.Create((_, _, _) =>
        {
            Assert.Equal(2, fixture.Store.SaveCount); // initial session + one checkpoint
            PermissionStateCheckpoint.EnsureSaved(fixture.Context);
            Interlocked.Increment(ref bodies);
            return Task.FromResult<object?>("ok");
        }, new() { Name = "write", FunctionPermission = AIFunctionPermissionDeclaration.Required("write") });
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[] { fixture.Middleware });
        var core = new FunctionExecutionCore(pipeline);
        var processor = new FunctionCallProcessor(fixture.Events, pipeline, core, 10,
            stateFactories: UnifiedPermissionModelTests.Factories());
        var execution = processor.ExecuteToolsAsync([], Enumerable.Range(0, calls)
            .Select(index => new FunctionCallContent("call-" + index, "write", new Dictionary<string, object?>())).ToList(),
            new ChatOptions { Tools = [function] }, fixture.Context.State, new(), fixture.Context, timeout.Token);
        var request = await inbox.Reader.ReadAsync(timeout.Token);
        fixture.Events.Respond(new PermissionResponseEvent(request.PermissionId, request.SourceName, "allow_session"));
        await execution;
        Assert.Equal(calls, bodies);
        Assert.Equal(2, fixture.Store.SaveCount);
        Assert.Single((await fixture.Journal()).OfType<PermissionRequestEvent>());
    }

    [Fact]
    public async Task StoreFailurePreventsEveryBatchBody()
    {
        await using var fixture = await Fixture.Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var inbox = fixture.Events.CreateInbox<PermissionRequestEvent>();
        var bodies = 0;
        var function = HPDAIFunctionFactory.Create((_, _, _) =>
        { bodies++; return Task.FromResult<object?>("ok"); }, new()
        { Name = "write", FunctionPermission = AIFunctionPermissionDeclaration.Required("write") });
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[] { fixture.Middleware });
        var processor = new FunctionCallProcessor(fixture.Events, pipeline, new FunctionExecutionCore(pipeline), 10,
            stateFactories: UnifiedPermissionModelTests.Factories());
        var execution = processor.ExecuteToolsAsync([], [new("one", "write", new Dictionary<string, object?>()),
            new("two", "write", new Dictionary<string, object?>())], new ChatOptions { Tools = [function] },
            fixture.Context.State, new(), fixture.Context, timeout.Token);
        var request = await inbox.Reader.ReadAsync(timeout.Token);
        fixture.Store.FailSave = true;
        fixture.Events.Respond(new PermissionResponseEvent(request.PermissionId, request.SourceName, "allow_session"));
        await Assert.ThrowsAsync<IOException>(() => execution);
        Assert.Equal(0, bodies);
        Assert.True(PermissionStateCheckpoint.IsPending(fixture.Context));
    }

    [Fact]
    public async Task UnrelatedSerializerFailureDoesNotBlockRequiredApprovalSave()
    {
        await using var fixture = await Fixture.Create();
        fixture.Context.UpdateState(state => state with { MiddlewareState = state.MiddlewareState
            .SetState(PermissionDecisionIndex.StateKey, new PermissionDecisionStateData()).SetState("other", new object()) });
        fixture.Session.MiddlewareState["other"] = "previous";
        PermissionStateCheckpoint.MarkPending(fixture.Context);
        var factories = UnifiedPermissionModelTests.Factories().ToDictionary(pair => pair.Key, pair => pair.Value);
        factories["other"] = new("other", typeof(object), "other", 1, true, StateScope.Session,
            _ => new object(), _ => throw new InvalidOperationException("unrelated serializer"));
        await PermissionStateCheckpoint.SaveAsync(fixture.Context, factories, CancellationToken.None);
        Assert.False(PermissionStateCheckpoint.IsPending(fixture.Context));
        Assert.Equal("previous", fixture.Session.MiddlewareState["other"]);
    }

    [Fact]
    public async Task InterruptingDenialPreventsApprovedAndUnadmittedBatchBodies()
    {
        await using var fixture = await Fixture.Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var inbox = fixture.Events.CreateInbox<PermissionRequestEvent>();
        var bodies = 0;
        var function = HPDAIFunctionFactory.Create((_, _, _) =>
        { bodies++; return Task.FromResult<object?>("ok"); }, new()
        { Name = "write", FunctionPermission = AIFunctionPermissionDeclaration.Required("write") });
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[] { fixture.Middleware });
        var processor = new FunctionCallProcessor(fixture.Events, pipeline, new FunctionExecutionCore(pipeline), 10,
            stateFactories: UnifiedPermissionModelTests.Factories());
        var execution = processor.ExecuteToolsAsync([], Enumerable.Range(0, 3)
            .Select(i => new FunctionCallContent("call-" + i, "write", new Dictionary<string, object?>())).ToList(),
            new ChatOptions { Tools = [function] }, fixture.Context.State, new(), fixture.Context, timeout.Token);
        var first = await inbox.Reader.ReadAsync(timeout.Token);
        fixture.Events.Respond(new PermissionResponseEvent(first.PermissionId, first.SourceName, "allow_once"));
        var second = await inbox.Reader.ReadAsync(timeout.Token);
        fixture.Events.Respond(new PermissionResponseEvent(second.PermissionId, second.SourceName, "deny_once"));
        await execution;
        Assert.Equal(0, bodies);
        Assert.True(fixture.Context.State.IsTerminated);
        Assert.Equal(2, (await fixture.Journal()).OfType<PermissionRequestEvent>().Count());
    }

    [Theory]
    [InlineData(PermissionDeniedBehavior.InterruptTurn)]
    [InlineData(PermissionDeniedBehavior.ReturnToModel)]
    public async Task PolicyDenialOutranksAutoapprovalAndHonorsBehavior(PermissionDeniedBehavior behavior)
    {
        await using var fixture = await Fixture.Create(policyResult: new()
        { Outcome = new PermissionDeny("Prohibited", behavior) });
        var result = await fixture.Admit("denied", CancellationToken.None,
            new() { Security = new() { Approval = AgentApprovalPolicy.AutoApprove } });
        if (behavior == PermissionDeniedBehavior.InterruptTurn) Assert.IsType<Reject>(result);
        else Assert.IsType<ContinueAfterFeedback>(result);
        var terminal = Assert.Single((await fixture.Journal()).OfType<PermissionResolvedEvent>());
        Assert.Equal(PermissionGrantSource.PolicyDecision, terminal.DecisionSource);
        Assert.Equal(PermissionDecisionKind.Deny, terminal.DecisionKind);
    }

    [Fact]
    public async Task ConstructionBypassSkipsPolicyAndRecordsOneResolution()
    {
        await using var fixture = await Fixture.Create(policyResult: new()
        { Outcome = new PermissionDeny("Prohibited") }, mode: AuthorizationMode.Bypass);
        var result = Assert.IsType<Invoke>(await fixture.Admit("bypass", CancellationToken.None));
        Assert.Equal(PermissionGrantSource.HostBypass, result.Grant!.Source);
        var journal = await fixture.Journal();
        Assert.Single(journal.OfType<PermissionResolvedEvent>());
        Assert.Empty(journal.OfType<PermissionEvaluatedEvent>());
        Assert.Empty(journal.OfType<PermissionRequestEvent>());
    }

    private sealed class ConstantPolicy(PermissionPolicyResult result) : IPermissionPolicy
    {
        public ValueTask<PermissionPolicyResult> EvaluateAsync(ValidatedPermissionInput input,
            PermissionEvaluationContext context, CancellationToken token) => ValueTask.FromResult(result);
    }
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }

    private sealed class CountingStore(ISessionStore inner) : ISessionStore
    {
        internal int SaveCount;
        internal bool FailSave;
        public AgentEventCodec EventCodec => inner.EventCodec;
        public Task<HPD.Agent.Session?> LoadSessionAsync(string id, CancellationToken ct = default) => inner.LoadSessionAsync(id, ct);
        public Task SaveSessionAsync(HPD.Agent.Session session, CancellationToken ct = default)
        { if (FailSave) throw new IOException("injected save failure"); SaveCount++; return inner.SaveSessionAsync(session, ct); }
        public ValueTask<bool> TryCreateSessionAsync(HPD.Agent.Session session, CancellationToken ct = default) => inner.TryCreateSessionAsync(session, ct);
        public ValueTask<SessionPreparationResult> TryPrepareSessionAsync(HPD.Agent.Session session, CancellationToken ct = default) => inner.TryPrepareSessionAsync(session, ct);
        public Task<List<string>> ListSessionIdsAsync(CancellationToken ct = default) => inner.ListSessionIdsAsync(ct);
        public Task DeleteSessionAsync(string id, CancellationToken ct = default) => inner.DeleteSessionAsync(id, ct);
        public ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(ThreadKey key, IReadOnlyList<AgentEvent> events, ThreadAppendCondition condition = default, CancellationToken ct = default) => inner.AppendThreadEventsAsync(key, events, condition, ct);
        public ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(ThreadKey key, IReadOnlyList<AgentEvent> events, ThreadJournalCursor cursor, CancellationToken ct = default) => inner.ReplaceThreadEventsAsync(key, events, cursor, ct);
        public ValueTask<ThreadDescriptor?> GetThreadAsync(ThreadKey key, CancellationToken ct = default) => inner.GetThreadAsync(key, ct);
        public IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(string id, ThreadListRequest request, CancellationToken ct = default) => inner.ListThreadsAsync(id, request, ct);
        public ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(ThreadKey key, CancellationToken ct = default) => inner.GetThreadEventHeadAsync(key, ct);
        public IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(ThreadKey key, ThreadEventReadRequest request, CancellationToken ct = default) => inner.ReadThreadEventsAsync(key, request, ct);
        public IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(ThreadKey key, ThreadJournalCursor cursor, ThreadObservationOptions options, CancellationToken ct = default) => inner.ObserveThreadEventsAsync(key, cursor, options, ct);
        public Task DeleteThreadAsync(string session, string thread, CancellationToken ct = default) => inner.DeleteThreadAsync(session, thread, ct);
        public Task<int> DeleteInactiveSessionsAsync(TimeSpan age, bool dryRun = false, CancellationToken ct = default) => inner.DeleteInactiveSessionsAsync(age, dryRun, ct);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal required CountingStore Store;
        internal required HPD.Agent.Session Session;
        internal required HPD.Agent.Thread Thread;
        internal required EventCoordinator Events;
        internal required AgentEventPublisher Publisher;
        internal required PermissionMiddleware Middleware;
        internal required AIFunction Function;
        internal required AgentContext Context;
        internal ThreadKey Key => new(Session.Id, Thread.Id);
        internal Task<CallDisposition> Admit(string call, CancellationToken token, AgentRunConfig? config = null) =>
            Middleware.AuthorizeCallAsync(Context.AsBeforeFunction(Function, call, new Dictionary<string, object?>(), config ?? new()),
                Middleware.ResolveFunction(Function, null, config ?? new()), call, new Dictionary<string, object?>(), null, config ?? new(), token).AsTask();
        internal AgentContext NewContext() => new("coding", Session.Id,
            AgentLoopState.Initial([], Guid.NewGuid().ToString(), Session.Id, "coding"), Events, Session, Thread,
            CancellationToken.None, Publisher);
        internal async Task<List<AgentEvent>> Journal()
        {
            var head = (await Store.GetThreadEventHeadAsync(Key))!;
            var values = new List<AgentEvent>();
            await foreach (var batch in Store.ReadThreadEventsAsync(Key,
                new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber))) values.AddRange(batch.Events);
            return values;
        }
        internal static async Task<Fixture> Create(bool stableId = true, PermissionPolicyResult? policyResult = null,
            AuthorizationMode mode = AuthorizationMode.Enforce)
        {
            Assert.True(AgentEventCompositionHost.TryComposeRegisteredModules(out var composition));
            var store = new CountingStore(new InMemorySessionStore(composition.Codec));
            var session = new HPD.Agent.Session(Guid.NewGuid().ToString()) { Store = store };
            var thread = session.CreateThread("coding", "main");
            await store.SaveSessionAsync(session);
            await store.SaveInitialThreadAsync(session.Id, thread);
            var events = new EventCoordinator();
            var publisher = new AgentEventPublisher(store, events);
            var function = HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"), new()
            {
                Name = "write", FunctionPermission = AIFunctionPermissionDeclaration.Required("write") with
                { PolicyDescriptorId = policyResult is null ? null : "test.policy.v1" },
                SchemaProvider = () => JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}").RootElement.Clone()
            });
            var context = new AgentContext("coding", session.Id,
                AgentLoopState.Initial([], "run", session.Id, "coding"), events, session, thread, CancellationToken.None, publisher, services: new EmptyServices());
            return new() { Store = store, Session = session, Thread = thread, Events = events, Publisher = publisher,
                Function = function, Context = context, Middleware = new(new AgentConfig { AgentId = stableId ? "coding" : null, Name = "coding" },
                    descriptorCatalog: new PermissionDescriptorCatalog(policyResult is null ? [] :
                    [new AIFunctionPermissionDescriptor { DescriptorId = "test.policy.v1", PolicyFactory = _ => new ConstantPolicy(policyResult) }]),
                    authorizationMode: mode) };
        }
        public ValueTask DisposeAsync() { Events.Dispose(); return ValueTask.CompletedTask; }
    }
}
