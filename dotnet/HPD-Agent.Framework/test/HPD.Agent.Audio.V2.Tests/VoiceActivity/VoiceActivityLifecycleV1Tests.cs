using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityLifecycleV1Tests
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
        var disposition = (VoiceActivityOpenExtentDispositionV1)dispositionValue;
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        var proof = Proof(operation, Plan(2, 2), disposition);

        var prepared = Assert.IsType<VoiceActivityPrepareReplacementResultV1.Prepared>(
            lifecycle.PrepareReplacement(1, proof));
        Assert.Equal(VoiceActivityLifecycleStateV1.ReplacementPrepared, prepared.Snapshot.State);
        Assert.Equal(operation, prepared.Snapshot.PendingOperation);

        var committed = Assert.IsType<VoiceActivityCommitReplacementResultV1.Applied>(
            lifecycle.CommitReplacement(2, operation, Stamp(2)));
        Assert.Equal(VoiceActivityLifecycleStateV1.SettlingPredecessor, committed.Snapshot.State);
        Assert.Equal(disposition, committed.Snapshot.LastOpenExtentDisposition);
        Assert.Equal(2UL, committed.Snapshot.Plan.PlanGeneration);
        Assert.Equal(1UL, committed.Snapshot.SourceGenerations["local"]);

        var settled = lifecycle.SettlePredecessor(operation, VoiceActivityReleaseDispositionV1.Confirmed);
        Assert.Equal(VoiceActivityLifecycleStateV1.Active, settled.State);
        Assert.Equal(VoiceActivityReleaseDispositionV1.Confirmed, settled.PredecessorRelease);
    }

    [Fact]
    public void Policy_specific_proofs_are_mandatory()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        var missingContinuity = new VoiceActivityReplacementProofV1(operation, Plan(2, 2), Stamp(1), Stamp(10),
            VoiceActivityOpenExtentDispositionV1.ContinueWithContinuityProof, true, true, null, null, false);
        Assert.Equal("continuity-proof-required", Assert.IsType<VoiceActivityPrepareReplacementResultV1.Rejected>(
            lifecycle.PrepareReplacement(1, missingContinuity)).SafeCode);

        var missingClose = new VoiceActivityReplacementProofV1(OperationId.Create(), Plan(2, 2), Stamp(1), Stamp(10),
            VoiceActivityOpenExtentDispositionV1.CloseByValidEvidence, true, true, null, null, false);
        Assert.Equal("valid-close-evidence-required", Assert.IsType<VoiceActivityPrepareReplacementResultV1.Rejected>(
            lifecycle.PrepareReplacement(1, missingClose)).SafeCode);

        var missingTransfer = new VoiceActivityReplacementProofV1(OperationId.Create(), Plan(2, 2), Stamp(1), Stamp(10),
            VoiceActivityOpenExtentDispositionV1.TransferCandidate, true, true, Hash("continuity"), null, false);
        Assert.Equal("transfer-proof-required", Assert.IsType<VoiceActivityPrepareReplacementResultV1.Rejected>(
            lifecycle.PrepareReplacement(1, missingTransfer)).SafeCode);
    }

    [Fact]
    public void Commit_is_a_synchronous_fenced_cut_and_expiry_quarantines()
    {
        var method = typeof(VoiceActivityLifecycleV1).GetMethod("CommitReplacement",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(typeof(VoiceActivityCommitReplacementResultV1), method.ReturnType);

        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen));
        var rejected = Assert.IsType<VoiceActivityCommitReplacementResultV1.Rejected>(
            lifecycle.CommitReplacement(2, operation, Stamp(11)));
        Assert.Equal("replacement-deadline-expired", rejected.SafeCode);
        Assert.Equal(VoiceActivityLifecycleStateV1.Quarantined, rejected.Snapshot.State);
    }

    [Fact]
    public void Exact_prepare_and_commit_retry_are_idempotent_but_changed_operations_conflict()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        var proof = Proof(operation, Plan(2, 2), VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen);
        lifecycle.PrepareReplacement(1, proof);
        Assert.IsType<VoiceActivityPrepareReplacementResultV1.Duplicate>(lifecycle.PrepareReplacement(1, proof));
        Assert.Equal("replacement-already-prepared", Assert.IsType<VoiceActivityPrepareReplacementResultV1.Rejected>(
            lifecycle.PrepareReplacement(2, Proof(OperationId.Create(), Plan(2, 2),
                VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen))).SafeCode);
        lifecycle.CommitReplacement(2, operation, Stamp(2));
        Assert.IsType<VoiceActivityCommitReplacementResultV1.Duplicate>(
            lifecycle.CommitReplacement(3, operation, Stamp(2)));
    }

    [Fact]
    public void Reset_is_an_ordered_discontinuity_barrier_and_fences_pending_candidate()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen));
        var reset = lifecycle.Reset(2, "operator-reset", Stamp(2));
        Assert.Equal(VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen, reset.LastOpenExtentDisposition);
        Assert.Null(reset.PendingOperation);
        Assert.Equal(2UL, reset.SourceGenerations["local"]);
        Assert.IsType<VoiceActivityCommitReplacementResultV1.Stale>(
            lifecycle.CommitReplacement(2, operation, Stamp(3)));
    }

    [Fact]
    public void Failed_settlement_quarantines_without_restoring_predecessor_authority()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen));
        lifecycle.CommitReplacement(2, operation, Stamp(2));
        var snapshot = lifecycle.SettlePredecessor(operation, VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed);
        Assert.Equal(2UL, snapshot.Plan.PlanGeneration);
        Assert.Equal(VoiceActivityLifecycleStateV1.Quarantined, snapshot.State);
        Assert.Equal("predecessor-release-unconfirmed", snapshot.SafeCode);
    }

    [Fact]
    public void Completion_is_immutable_and_late_release_is_observation_only()
    {
        var lifecycle = Lifecycle();
        var completed = lifecycle.Complete(VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed);
        Assert.Equal(VoiceActivityLifecycleStateV1.Completed, completed.State);
        Assert.True(lifecycle.ObserveLateRelease(OperationId.Create(), VoiceActivityReleaseDispositionV1.Confirmed));
        Assert.Equal(completed.LifecycleRevision, lifecycle.Current.LifecycleRevision);
        Assert.Equal(completed.State, lifecycle.Current.State);
        Assert.Equal(completed.Plan, lifecycle.Current.Plan);
        Assert.Equal(completed.PredecessorRelease, lifecycle.Current.PredecessorRelease);
        Assert.IsType<VoiceActivityPrepareReplacementResultV1.Stale>(lifecycle.PrepareReplacement(
            completed.LifecycleRevision, Proof(OperationId.Create(), Plan(2, 2),
                VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen)));
    }

    [Fact]
    public void Concurrent_commits_linearize_one_authority_successor()
    {
        var lifecycle = Lifecycle();
        var operation = OperationId.Create();
        lifecycle.PrepareReplacement(1, Proof(operation, Plan(2, 2),
            VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen));
        var results = Enumerable.Range(0, 32).AsParallel().Select(_ =>
            lifecycle.CommitReplacement(2, operation, Stamp(2))).ToArray();
        Assert.Equal(1, results.Count(static row => row is VoiceActivityCommitReplacementResultV1.Applied));
        Assert.Equal(31, results.Count(static row => row is VoiceActivityCommitReplacementResultV1.Duplicate));
        Assert.Equal(2UL, lifecycle.Current.Plan.PlanGeneration);
    }

    private static VoiceActivityLifecycleV1 Lifecycle() => new(Session, GraphDirectionV1.IngressForward,
        Plan(1, 1), new Dictionary<string, ulong> { ["local"] = 1 });

    private static VoiceActivityReplacementProofV1 Proof(OperationId operation,
        VoiceActivityEffectivePlanV1 candidate, VoiceActivityOpenExtentDispositionV1 disposition) => new(
        operation, candidate, Stamp(1), Stamp(10), disposition, true, true,
        disposition is VoiceActivityOpenExtentDispositionV1.ContinueWithContinuityProof or
            VoiceActivityOpenExtentDispositionV1.TransferCandidate ? Hash("continuity") : null,
        disposition == VoiceActivityOpenExtentDispositionV1.TransferCandidate ? Hash("transfer") : null,
        disposition == VoiceActivityOpenExtentDispositionV1.CloseByValidEvidence);

    private static VoiceActivityEffectivePlanV1 Plan(ulong generation, ulong revision)
    {
        var request = new VoiceActivityRequestV1(VoiceActivityProfileV1.HpdManaged,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, null,
            [new("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true)],
            ActivityDegradationPolicyV1.Strict,
            new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var candidate = new VoiceActivitySourceCandidateV1("local", ActivitySourceKindV1.LocalDetector,
            new VoiceActivitySourceCapabilitiesV1(VoiceActivityInputOwnershipV1.BorrowedSynchronous,
                [new(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
                new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
                new(VoiceActivityMeasurementKindV1.EngineScore, new BoundedAscii("score"), -1, 1, null),
                VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
                VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
                VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
                true, false, 1), true, ProviderActivityVisibilityV1.AcceptedLocally);
        return Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(
            VoiceActivityPlanCompilerV1.Compile(generation, revision, request, [candidate])).Plan;
    }

    private static MonotonicStampV1 Stamp(ulong value) => new(Clock, Boot, value);
    private static Hash256 Hash(string value) => Hash256.Compute(System.Text.Encoding.ASCII.GetBytes(value));
}
