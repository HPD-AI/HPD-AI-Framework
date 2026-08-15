using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityReplacementCoordinatorV1Tests
{
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();

    [Fact]
    public async Task Target_is_prepared_and_started_before_cut_then_predecessor_settles()
    {
        var fixture = Fixture();
        var order = new List<string>();
        fixture.Candidate.OnPrepare = () => { order.Add("target-prepare"); Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration); };
        fixture.Candidate.OnStart = () => { order.Add("target-start"); Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration); };
        fixture.Active.OnDrain = () => { order.Add("source-drain"); Assert.Equal(2UL, fixture.Lifecycle.Current.Plan.PlanGeneration); };
        fixture.Active.OnTerminate = () => { order.Add("source-terminate"); Assert.Equal(2UL, fixture.Lifecycle.Current.Plan.PlanGeneration); };

        var result = Assert.IsType<VoiceActivityParticipantReplacementResultV1.Applied>(await fixture.Coordinator.ReplaceAsync(
            fixture.Candidate, fixture.CandidateContext, fixture.Request, default, default));

        Assert.Equal(["target-prepare", "target-start", "source-drain", "source-terminate"], order);
        Assert.Equal(VoiceActivityReleaseDispositionV1.Confirmed, result.PredecessorRelease);
        Assert.Equal(VoiceActivityLifecycleStateV1.Active, result.Snapshot.State);
        Assert.Equal(2UL, result.Snapshot.Plan.PlanGeneration);
    }

    [Fact]
    public async Task Candidate_start_failure_rolls_back_without_authority_cut()
    {
        var fixture = Fixture();
        fixture.Candidate.StartDisposition = RuntimeParticipantDispositionV1.Failed;
        var result = Assert.IsType<VoiceActivityParticipantReplacementResultV1.Rejected>(await fixture.Coordinator.ReplaceAsync(
            fixture.Candidate, fixture.CandidateContext, fixture.Request, default, default));
        Assert.Equal("candidate-start-failed", result.SafeCode);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
        Assert.Equal(1, fixture.Candidate.TerminateCalls);
        Assert.Equal(0, fixture.Active.DrainCalls);
    }

    [Fact]
    public async Task Caller_cancellation_before_cut_rolls_back_candidate()
    {
        var fixture = Fixture();
        using var caller = new CancellationTokenSource();
        fixture.Candidate.OnStart = caller.Cancel;
        Assert.IsType<VoiceActivityParticipantReplacementResultV1.Cancelled>(await fixture.Coordinator.ReplaceAsync(
            fixture.Candidate, fixture.CandidateContext, fixture.Request, caller.Token, default));
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
        Assert.Equal(1, fixture.Candidate.TerminateCalls);
    }

    [Fact]
    public async Task Convergence_cancellation_after_cut_quarantines_without_rollback()
    {
        var fixture = Fixture();
        using var convergence = new CancellationTokenSource();
        convergence.Cancel();
        fixture.Active.ThrowCancellationOnDrain = true;
        var result = Assert.IsType<VoiceActivityParticipantReplacementResultV1.Applied>(await fixture.Coordinator.ReplaceAsync(
            fixture.Candidate, fixture.CandidateContext, fixture.Request, default, convergence.Token));
        Assert.Equal(VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed, result.PredecessorRelease);
        Assert.Equal(VoiceActivityLifecycleStateV1.Quarantined, result.Snapshot.State);
        Assert.Equal(2UL, result.Snapshot.Plan.PlanGeneration);
    }

    [Fact]
    public async Task Exact_operation_retry_has_no_duplicate_effects()
    {
        var fixture = Fixture();
        await fixture.Coordinator.ReplaceAsync(fixture.Candidate, fixture.CandidateContext, fixture.Request, default, default);
        Assert.IsType<VoiceActivityParticipantReplacementResultV1.Duplicate>(await fixture.Coordinator.ReplaceAsync(
            fixture.Candidate, fixture.CandidateContext, fixture.Request, default, default));
        Assert.Equal(1, fixture.Candidate.PrepareCalls);
        Assert.Equal(1, fixture.Candidate.StartCalls);
        Assert.Equal(1, fixture.Active.DrainCalls);
        Assert.Equal(1, fixture.Active.TerminateCalls);
    }

    [Fact]
    public async Task Grant_mismatch_rejects_before_candidate_effects()
    {
        var fixture = Fixture();
        var otherContext = new RuntimeParticipantContextV1(ParticipantId.Create(), fixture.CandidateContext.Authority);
        var result = Assert.IsType<VoiceActivityParticipantReplacementResultV1.Rejected>(await fixture.Coordinator.ReplaceAsync(
            fixture.Candidate, otherContext, fixture.Request, default, default));
        Assert.Equal("replacement-target-invalid", result.SafeCode);
        Assert.Equal(0, fixture.Candidate.PrepareCalls);
    }

    [Fact]
    public async Task Completion_serializes_after_inflight_replacement_and_remains_terminal()
    {
        var fixture = Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Candidate.StartBarrier = async () => { entered.SetResult(); await release.Task; };
        var replacement = fixture.Coordinator.ReplaceAsync(fixture.Candidate, fixture.CandidateContext,
            fixture.Request, default, default).AsTask();
        await entered.Task;
        var completion = fixture.Coordinator.CompleteAsync(VoiceActivityReleaseDispositionV1.Confirmed, default).AsTask();
        Assert.False(completion.IsCompleted);
        release.SetResult();
        await replacement;
        var completed = await completion;
        Assert.Equal(VoiceActivityLifecycleStateV1.Completed, completed.State);
        Assert.Equal(2UL, completed.Plan.PlanGeneration);
    }

    private static ReplacementFixture Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        var activeContext = new RuntimeParticipantContextV1(ParticipantId.Create(), authority);
        var candidateContext = new RuntimeParticipantContextV1(ParticipantId.Create(), authority);
        var active = new ScriptedParticipant("active", activeContext);
        var candidate = new ScriptedParticipant("candidate", candidateContext);
        var lifecycle = new VoiceActivityLifecycleV1(session, GraphDirectionV1.IngressForward, Plan(1, 1),
            new Dictionary<string, ulong> { ["local"] = 1 });
        var operation = OperationId.Create();
        var grant = new VoiceActivityConditionalReplacementGrantV1(session, operation, 1,
            candidate.Descriptor.Id, candidateContext.ParticipantId, Stamp(10), Hash("grant"));
        var request = new VoiceActivityParticipantReplacementRequestV1(Plan(2, 2), grant, Stamp(1), Stamp(2),
            VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen, null, null, false);
        var coordinator = new VoiceActivityReplacementCoordinatorV1(lifecycle, active, active.Handle);
        return new(coordinator, lifecycle, active, candidate, candidateContext, request);
    }

    private sealed record ReplacementFixture(VoiceActivityReplacementCoordinatorV1 Coordinator,
        VoiceActivityLifecycleV1 Lifecycle, ScriptedParticipant Active, ScriptedParticipant Candidate,
        RuntimeParticipantContextV1 CandidateContext, VoiceActivityParticipantReplacementRequestV1 Request);

    private sealed class ScriptedParticipant : IRuntimeParticipantV1
    {
        internal ScriptedParticipant(string id, RuntimeParticipantContextV1 context)
        {
            Descriptor = DescriptorFor(id);
            Handle = new RuntimePreparedHandleV1(Descriptor.Id, context);
        }

        public RuntimeParticipantDescriptorV1 Descriptor { get; }
        internal RuntimePreparedHandleV1 Handle { get; }
        internal RuntimeParticipantDispositionV1 PrepareDisposition { get; set; } = RuntimeParticipantDispositionV1.Succeeded;
        internal RuntimeParticipantDispositionV1 StartDisposition { get; set; } = RuntimeParticipantDispositionV1.Succeeded;
        internal RuntimeParticipantDispositionV1 DrainDisposition { get; set; } = RuntimeParticipantDispositionV1.Succeeded;
        internal RuntimeParticipantDispositionV1 TerminateDisposition { get; set; } = RuntimeParticipantDispositionV1.Succeeded;
        internal int PrepareCalls { get; private set; }
        internal int StartCalls { get; private set; }
        internal int DrainCalls { get; private set; }
        internal int TerminateCalls { get; private set; }
        internal Action? OnPrepare { get; set; }
        internal Action? OnStart { get; set; }
        internal Action? OnDrain { get; set; }
        internal Action? OnTerminate { get; set; }
        internal Func<Task>? StartBarrier { get; set; }
        internal bool ThrowCancellationOnDrain { get; set; }

        public ValueTask<RuntimeParticipantPrepareResultV1> PrepareAsync(RuntimeParticipantContextV1 context,
            CancellationToken cancellationToken)
        {
            PrepareCalls++;
            OnPrepare?.Invoke();
            return ValueTask.FromResult(new RuntimeParticipantPrepareResultV1(PrepareDisposition,
                new BoundedAscii("prepare"), PrepareDisposition == RuntimeParticipantDispositionV1.Succeeded ? Handle : null));
        }

        public async ValueTask<RuntimeParticipantResultV1> StartAsync(RuntimePreparedHandleV1 handle,
            CancellationToken cancellationToken)
        {
            StartCalls++;
            OnStart?.Invoke();
            if (StartBarrier is not null) await StartBarrier();
            return Result(StartDisposition, "start");
        }

        public ValueTask<RuntimeParticipantResultV1> DrainAsync(RuntimeDrainIntentV1 intent,
            CancellationToken cancellationToken)
        {
            DrainCalls++;
            OnDrain?.Invoke();
            if (ThrowCancellationOnDrain) return ValueTask.FromCanceled<RuntimeParticipantResultV1>(cancellationToken);
            return ValueTask.FromResult(Result(DrainDisposition, "drain"));
        }

        public ValueTask<RuntimeParticipantResultV1> TerminateAsync(RuntimeTerminationCauseV1 cause,
            CancellationToken cancellationToken)
        {
            TerminateCalls++;
            OnTerminate?.Invoke();
            return ValueTask.FromResult(Result(TerminateDisposition, "terminate"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static RuntimeParticipantResultV1 Result(RuntimeParticipantDispositionV1 disposition, string code) =>
            new(disposition, new BoundedAscii(code));
    }

    private static RuntimeParticipantDescriptorV1 DescriptorFor(string id) => new(
        new BoundedAscii(id), new BoundedAscii("Audio"), new BoundedAscii("VoiceActivity"), [],
        AuthorityAxisId.Graph, new DurationNs(1_000_000_000), new DurationNs(1_000_000_000),
        new DurationNs(1_000_000_000), new DurationNs(1_000_000_000), []);

    private static VoiceActivityEffectivePlanV1 Plan(ulong generation, ulong revision)
    {
        var request = new VoiceActivityRequestV1(VoiceActivityProfileV1.HpdManaged,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, null,
            [new("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true)],
            ActivityDegradationPolicyV1.Strict,
            new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var capabilities = new VoiceActivitySourceCapabilitiesV1(VoiceActivityInputOwnershipV1.BorrowedSynchronous,
            [new(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
            new(VoiceActivityMeasurementKindV1.EngineScore, new BoundedAscii("score"), -1, 1, null),
            VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
            true, false, 1);
        return Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(VoiceActivityPlanCompilerV1.Compile(
            generation, revision, request, [new("local", ActivitySourceKindV1.LocalDetector, capabilities, true,
                ProviderActivityVisibilityV1.AcceptedLocally)])).Plan;
    }

    private static MonotonicStampV1 Stamp(ulong value) => new(Clock, Boot, value);
    private static Hash256 Hash(string value) => Hash256.Compute(System.Text.Encoding.ASCII.GetBytes(value));
}
