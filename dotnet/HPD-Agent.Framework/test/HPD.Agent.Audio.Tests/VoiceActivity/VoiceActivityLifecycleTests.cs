using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityLifecycleTests
{
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();
    private static readonly SessionAuthorityStampV1 Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void All_four_open_extent_dispositions_prepare_commit_and_settle(int dispositionValue)
    {
        var disposition = (VoiceActivityOpenExtentDisposition)dispositionValue;
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        var proof = Proof(operation, Plan(2, 2), disposition);

        var prepared = Assert.IsType<VoiceActivityPrepareReplacementResult.Prepared>(
            lifecycle.PrepareReplacement(1, proof));
        Assert.Equal(VoiceActivityLifecycleState.ReplacementPrepared, prepared.Snapshot.State);
        Assert.Equal(operation, prepared.Snapshot.PendingOperation);

        var committed = Assert.IsType<VoiceActivityCommitReplacementResult.Applied>(
            lifecycle.CommitReplacement(2, operation, Stamp(2)));
        Assert.Equal(VoiceActivityLifecycleState.SettlingPredecessor, committed.Snapshot.State);
        Assert.Equal(disposition, committed.Snapshot.LastOpenExtentDisposition);
        Assert.Equal(2UL, committed.Snapshot.Plan.PlanGeneration);
        Assert.Equal(1UL, committed.Snapshot.SourceGenerations["local"]);

        var settled = lifecycle.SettlePredecessor(operation, VoiceActivityReleaseDisposition.Confirmed);
        Assert.Equal(VoiceActivityLifecycleState.Active, settled.State);
        Assert.Equal(VoiceActivityReleaseDisposition.Confirmed, settled.PredecessorRelease);
    }

    [Fact]
    public void Policy_specific_proofs_are_mandatory()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        var missingContinuity = new VoiceActivityReplacementProof(operation, Plan(2, 2), Stamp(1), Stamp(10),
            VoiceActivityOpenExtentDisposition.ContinueWithContinuityProof, true, true, null, null, false);
        Assert.Equal("continuity-proof-required", Assert.IsType<VoiceActivityPrepareReplacementResult.Rejected>(
            lifecycle.PrepareReplacement(1, missingContinuity)).SafeCode);

        var missingClose = new VoiceActivityReplacementProof(OperationId.Create(), Plan(2, 2), Stamp(1), Stamp(10),
            VoiceActivityOpenExtentDisposition.CloseByValidEvidence, true, true, null, null, false);
        Assert.Equal("valid-close-evidence-required", Assert.IsType<VoiceActivityPrepareReplacementResult.Rejected>(
            lifecycle.PrepareReplacement(1, missingClose)).SafeCode);

        var missingTransfer = new VoiceActivityReplacementProof(OperationId.Create(), Plan(2, 2), Stamp(1), Stamp(10),
            VoiceActivityOpenExtentDisposition.TransferCandidate, true, true, Hash("continuity"), null, false);
        Assert.Equal("transfer-proof-required", Assert.IsType<VoiceActivityPrepareReplacementResult.Rejected>(
            lifecycle.PrepareReplacement(1, missingTransfer)).SafeCode);
    }

    [Fact]
    public void Commit_is_a_synchronous_fenced_cut_and_expiry_quarantines()
    {
        var method = typeof(VoiceActivityLifecycle).GetMethod("CommitReplacement",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(typeof(VoiceActivityCommitReplacementResult), method.ReturnType);

        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen));
        var rejected = Assert.IsType<VoiceActivityCommitReplacementResult.Rejected>(
            lifecycle.CommitReplacement(2, operation, Stamp(11)));
        Assert.Equal("replacement-deadline-expired", rejected.SafeCode);
        Assert.Equal(VoiceActivityLifecycleState.Quarantined, rejected.Snapshot.State);
    }

    [Fact]
    public void Exact_prepare_and_commit_retry_are_idempotent_but_changed_operations_conflict()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        var proof = Proof(operation, Plan(2, 2), VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen);
        lifecycle.PrepareReplacement(1, proof);
        Assert.IsType<VoiceActivityPrepareReplacementResult.Duplicate>(lifecycle.PrepareReplacement(1, proof));
        Assert.Equal("replacement-already-prepared", Assert.IsType<VoiceActivityPrepareReplacementResult.Rejected>(
            lifecycle.PrepareReplacement(2, Proof(OperationId.Create(), Plan(2, 2),
                VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen))).SafeCode);
        lifecycle.CommitReplacement(2, operation, Stamp(2));
        Assert.IsType<VoiceActivityCommitReplacementResult.Duplicate>(
            lifecycle.CommitReplacement(3, operation, Stamp(2)));
    }

    [Fact]
    public void Reset_is_an_ordered_discontinuity_barrier_and_fences_pending_candidate()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen));
        var reset = lifecycle.Reset(2, "operator-reset", Stamp(2));
        Assert.Equal(VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen, reset.LastOpenExtentDisposition);
        Assert.Null(reset.PendingOperation);
        Assert.Equal(2UL, reset.SourceGenerations["local"]);
        Assert.IsType<VoiceActivityCommitReplacementResult.Stale>(
            lifecycle.CommitReplacement(2, operation, Stamp(3)));
    }

    [Fact]
    public void Failed_settlement_quarantines_without_restoring_predecessor_authority()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen));
        lifecycle.CommitReplacement(2, operation, Stamp(2));
        var snapshot = lifecycle.SettlePredecessor(operation, VoiceActivityReleaseDisposition.ReleaseUnconfirmed);
        Assert.Equal(2UL, snapshot.Plan.PlanGeneration);
        Assert.Equal(VoiceActivityLifecycleState.Quarantined, snapshot.State);
        Assert.Equal("predecessor-release-unconfirmed", snapshot.SafeCode);
    }

    [Fact]
    public void Completion_is_immutable_and_late_release_is_observation_only()
    {
        var lifecycle = Lifecycle();
        var completed = lifecycle.Complete(VoiceActivityReleaseDisposition.ReleaseUnconfirmed);
        Assert.Equal(VoiceActivityLifecycleState.Completed, completed.State);
        Assert.True(lifecycle.ObserveLateRelease(OperationId.Create(), VoiceActivityReleaseDisposition.Confirmed));
        Assert.Equal(completed.LifecycleRevision, lifecycle.Current.LifecycleRevision);
        Assert.Equal(completed.State, lifecycle.Current.State);
        Assert.Equal(completed.Plan, lifecycle.Current.Plan);
        Assert.Equal(completed.PredecessorRelease, lifecycle.Current.PredecessorRelease);
        Assert.IsType<VoiceActivityPrepareReplacementResult.Stale>(lifecycle.PrepareReplacement(
            completed.LifecycleRevision, Proof(OperationId.Create(), Plan(2, 2),
                VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen)));
    }

    [Fact]
    public void Concurrent_commits_linearize_one_authority_successor()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen));
        var results = Enumerable.Range(0, 32).AsParallel().Select(_ =>
            lifecycle.CommitReplacement(2, operation, Stamp(2))).ToArray();
        Assert.Equal(1, results.Count(static row => row is VoiceActivityCommitReplacementResult.Applied));
        Assert.Equal(31, results.Count(static row => row is VoiceActivityCommitReplacementResult.Duplicate));
        Assert.Equal(2UL, lifecycle.Current.Plan.PlanGeneration);
    }

    private static VoiceActivityLifecycle Lifecycle() => new(Session, GraphDirection.IngressForward,
        Plan(1, 1), new Dictionary<string, ulong> { ["local"] = 1 });

    private static VoiceActivityReplacementProof Proof(OperationId operation,
        VoiceActivityEffectivePlan candidate, VoiceActivityOpenExtentDisposition disposition) => new(
        operation, candidate, Stamp(1), Stamp(10), disposition, true, true,
        disposition is VoiceActivityOpenExtentDisposition.ContinueWithContinuityProof or
            VoiceActivityOpenExtentDisposition.TransferCandidate ? Hash("continuity") : null,
        disposition == VoiceActivityOpenExtentDisposition.TransferCandidate ? Hash("transfer") : null,
        disposition == VoiceActivityOpenExtentDisposition.CloseByValidEvidence);

    private static VoiceActivityEffectivePlan Plan(ulong generation, ulong revision)
    {
        var request = new VoiceActivityRequest(VoiceActivityProfile.HpdManaged,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, null,
            [new("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true)],
            ActivityDegradationPolicy.Strict,
            new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var candidate = new VoiceActivitySourceCandidate("local", ActivitySourceKind.LocalDetector,
            new VoiceActivitySourceCapabilities(VoiceActivityInputOwnership.BorrowedSynchronous,
                [new(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
                new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
                new(VoiceActivityMeasurementKind.EngineScore, new BoundedAscii("score"), -1, 1, null),
                VoiceActivitySourceStateModel.Stateless, VoiceActivitySourceConcurrency.Serial,
                VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
                VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.ReplacementRequired,
                true, false, 1), true, ProviderActivityVisibility.AcceptedLocally);
        return Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(
            VoiceActivityPlanCompiler.Compile(generation, revision, request, [candidate])).Plan;
    }

    private static MonotonicStampV1 Stamp(ulong value) => new(Clock, Boot, value);
    private static Hash256 Hash(string value) => Hash256.Compute(System.Text.Encoding.ASCII.GetBytes(value));
}
