using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityUpdateTransactionV1Tests
{
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();

    [Fact]
    public void Applied_update_has_a_real_ordered_lifecycle_cut()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Staged,
            fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
                [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
                ProviderActivityVisibilityV1.Acknowledged).Disposition);
        var applied = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Applied, applied.Disposition);
        Assert.Equal((1UL, 2UL), (applied.OldRevision, applied.NewRevision));
        Assert.Equal((1UL, 2UL), (applied.OldPlanGeneration, applied.NewPlanGeneration));
        Assert.Equal(2UL, fixture.Lifecycle.Current.LifecycleRevision);
        Assert.Equal(2UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Fact]
    public void Replacement_only_update_does_not_mutate_effective_plan()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.RequiresReplacement)], VoiceActivityCurrentSpeechHandlingV1.MarkDiscontinuousAtCut,
            ProviderActivityVisibilityV1.Unknown);
        var result = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.RequiresReplacement, result.Disposition);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
        Assert.Contains("update-requires-replacement", result.Warnings);
    }

    [Fact]
    public void Mixed_field_truth_is_partially_applied_without_flattening()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied),
                Field(VoiceActivityUpdateFieldDispositionV1.RequestedUnconfirmed,
                    VoiceActivityUpdateFieldV1.NoiseEnvironment)],
            VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut, ProviderActivityVisibilityV1.NotObservable);
        var result = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.PartiallyApplied, result.Disposition);
        Assert.True(result.Degraded);
        Assert.Equal(VoiceActivityUpdateFieldDispositionV1.RequestedUnconfirmed,
            result.Fields.Single(field => field.Field == VoiceActivityUpdateFieldV1.NoiseEnvironment).Disposition);
        Assert.Equal(2UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Rejected_field_prevents_staging_or_effect()
    {
        var fixture = Fixture();
        var result = fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Rejected)], VoiceActivityCurrentSpeechHandlingV1.ResetAtCut,
            ProviderActivityVisibilityV1.Rejected);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected, result.Disposition);
        Assert.True(result.RolledBack);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Concurrent_second_update_is_superseded_not_last_writer_wins()
    {
        var fixture = Fixture();
        fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        var second = fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Superseded, second.Disposition);
        Assert.All(second.Fields, field =>
            Assert.Equal(VoiceActivityUpdateFieldDispositionV1.Superseded, field.Disposition));
    }

    [Fact]
    public void Stale_revision_is_rejected_by_fence()
    {
        var fixture = Fixture();
        var result = fixture.Sequencer.Stage(OperationId.Create(), 99, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ResetAtCut,
            ProviderActivityVisibilityV1.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.RejectedByFence, result.Disposition);
        Assert.Equal(result.OldRevision, result.NewRevision);
    }

    [Fact]
    public void Reset_between_stage_and_commit_rejects_by_fence()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        fixture.Lifecycle.Reset(1, "test-reset", Stamp(2));
        var result = fixture.Sequencer.Commit(operation, Stamp(3));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.RejectedByFence, result.Disposition);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Fact]
    public void Stop_between_stage_and_commit_is_completed_before_commit()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        fixture.Lifecycle.Complete(VoiceActivityReleaseDispositionV1.Confirmed);
        var result = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.CompletedBeforeCommit, result.Disposition);
        Assert.Equal(VoiceActivityLifecycleStateV1.Completed, fixture.Lifecycle.Current.State);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Fact]
    public void Replacement_prepare_between_stage_and_commit_fences_update()
    {
        var fixture = Fixture();
        var update = OperationId.Create();
        fixture.Sequencer.Stage(update, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        var replacement = new VoiceActivityReplacementProofV1(OperationId.Create(), fixture.Candidate,
            Stamp(2), Stamp(10), VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen,
            true, true, null, null, false);
        Assert.IsType<VoiceActivityPrepareReplacementResultV1.Prepared>(
            fixture.Lifecycle.PrepareReplacement(1, replacement));
        var result = fixture.Sequencer.Commit(update, Stamp(3));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.RejectedByFence, result.Disposition);
        Assert.Equal(VoiceActivityLifecycleStateV1.ReplacementPrepared, fixture.Lifecycle.Current.State);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Theory]
    [InlineData((int)VoiceActivityUpdateFieldV1.Profile)]
    [InlineData((int)VoiceActivityUpdateFieldV1.PrefixContext)]
    [InlineData((int)VoiceActivityUpdateFieldV1.Sources)]
    [InlineData((int)VoiceActivityUpdateFieldV1.Limits)]
    [InlineData((int)VoiceActivityUpdateFieldV1.Calibration)]
    [InlineData((int)VoiceActivityUpdateFieldV1.Authority)]
    public void Structural_fields_cannot_claim_in_place_application(int fieldValue)
    {
        var fixture = Fixture();
        var field = (VoiceActivityUpdateFieldV1)fieldValue;
        var result = fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied, field)], VoiceActivityCurrentSpeechHandlingV1.ResetAtCut,
            ProviderActivityVisibilityV1.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected, result.Disposition);
        Assert.Equal("field-requires-replacement", result.Fields.Single().Reason);
    }

    [Fact]
    public void Source_without_sequenced_capability_cannot_claim_applied()
    {
        var current = Plan(1, 1, ActivityResponsivenessV1.Balanced,
            VoiceActivitySourceControlV1.Unsupported);
        var lifecycle = new VoiceActivityLifecycleV1(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirectionV1.IngressForward, current, new Dictionary<string, ulong> { ["local"] = 1 });
        var sequencer = new VoiceActivityUpdateSequencerV1(lifecycle);
        var result = sequencer.Stage(OperationId.Create(), 1,
            Plan(2, 2, ActivityResponsivenessV1.Responsive, VoiceActivitySourceControlV1.Unsupported),
            Stamp(1), [Field(VoiceActivityUpdateFieldDispositionV1.Applied)], VoiceActivityCurrentSpeechHandlingV1.ResetAtCut,
            ProviderActivityVisibilityV1.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected, result.Disposition);
        Assert.Equal("sequenced-update-unsupported", result.Fields.Single().Reason);
    }

    [Fact]
    public void Terminal_exact_retry_replays_but_changed_operation_intent_is_rejected()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        var fields = new[] { Field(VoiceActivityUpdateFieldDispositionV1.Applied) };
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1), fields,
            VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut, ProviderActivityVisibilityV1.Acknowledged);
        var applied = fixture.Sequencer.Commit(operation, Stamp(2));
        var retry = fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(3), fields,
            VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut, ProviderActivityVisibilityV1.Acknowledged);
        Assert.Same(applied, retry);
        var changed = fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(4),
            [Field(VoiceActivityUpdateFieldDispositionV1.RequiresReplacement)],
            VoiceActivityCurrentSpeechHandlingV1.MarkDiscontinuousAtCut, ProviderActivityVisibilityV1.Unknown);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected, changed.Disposition);
        Assert.Contains("update-operation-contradiction", changed.Warnings);
        Assert.Equal(2UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Commit_cut_cannot_move_before_stage_observation()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(5),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)],
            VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        var result = fixture.Sequencer.Commit(operation, Stamp(4));
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected, result.Disposition);
        Assert.Contains("update-cut-invalid", result.Warnings);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Commit_cut_on_an_incomparable_clock_is_rejected()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDispositionV1.Applied)],
            VoiceActivityCurrentSpeechHandlingV1.ContinueUnderOldUntilCut,
            ProviderActivityVisibilityV1.Acknowledged);
        var incomparable = new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 2);
        var result = fixture.Sequencer.Commit(operation, incomparable);
        Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected, result.Disposition);
        Assert.Contains("update-cut-invalid", result.Warnings);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Terminal_retry_history_is_strictly_bounded()
    {
        var lifecycle = new VoiceActivityLifecycleV1(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirectionV1.IngressForward, Plan(1, 1, ActivityResponsivenessV1.Balanced),
            new Dictionary<string, ulong> { ["local"] = 1 });
        var sequencer = new VoiceActivityUpdateSequencerV1(lifecycle, 2);
        var candidate = Plan(2, 2, ActivityResponsivenessV1.Responsive);
        for (var index = 0; index < 3; index++)
            Assert.Equal(VoiceActivityDetailedUpdateDispositionV1.Rejected,
                sequencer.Stage(OperationId.Create(), 1, candidate, Stamp((ulong)index + 1),
                    [Field(VoiceActivityUpdateFieldDispositionV1.Rejected)],
                    VoiceActivityCurrentSpeechHandlingV1.ResetAtCut,
                    ProviderActivityVisibilityV1.Rejected).Disposition);
        Assert.Equal(2, sequencer.TerminalHistoryCount);
    }

    private static UpdateFixture Fixture()
    {
        var lifecycle = new VoiceActivityLifecycleV1(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirectionV1.IngressForward, Plan(1, 1, ActivityResponsivenessV1.Balanced),
            new Dictionary<string, ulong> { ["local"] = 1 });
        return new(lifecycle, new VoiceActivityUpdateSequencerV1(lifecycle),
            Plan(2, 2, ActivityResponsivenessV1.Responsive));
    }

    private static VoiceActivityUpdateFieldResultV1 Field(VoiceActivityUpdateFieldDispositionV1 disposition,
        VoiceActivityUpdateFieldV1 field = VoiceActivityUpdateFieldV1.Responsiveness) => new(field,
        VoiceActivityUpdateFieldOwnerV1.Source, "local", "responsive",
        disposition == VoiceActivityUpdateFieldDispositionV1.Applied ? "responsive" : "balanced",
        disposition, disposition == VoiceActivityUpdateFieldDispositionV1.Applied ? "source-sequenced" : "not-proven");

    private static VoiceActivityEffectivePlanV1 Plan(ulong generation, ulong revision,
        ActivityResponsivenessV1 responsiveness,
        VoiceActivitySourceControlV1 dynamicUpdate = VoiceActivitySourceControlV1.Sequenced)
    {
        var request = new VoiceActivityRequestV1(VoiceActivityProfileV1.HpdManaged, responsiveness,
            VoiceActivityNoiseEnvironmentV1.Variable, VoiceActivitySpeechContinuityV1.Natural, null,
            [new("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true)],
            ActivityDegradationPolicyV1.Strict,
            new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var capabilities = new VoiceActivitySourceCapabilitiesV1(VoiceActivityInputOwnershipV1.BorrowedSynchronous,
            [new(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
            new(VoiceActivityMeasurementKindV1.EngineScore, new BoundedAscii("score"), -1, 1, null),
            VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
            dynamicUpdate, VoiceActivitySourceControlV1.Sequenced,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
            true, false, 1);
        return Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(VoiceActivityPlanCompilerV1.Compile(
            generation, revision, request, [new("local", ActivitySourceKindV1.LocalDetector, capabilities, true,
                ProviderActivityVisibilityV1.Acknowledged)])).Plan;
    }

    private static MonotonicStampV1 Stamp(ulong value) => new(Clock, Boot, value);

    private sealed record UpdateFixture(VoiceActivityLifecycleV1 Lifecycle,
        VoiceActivityUpdateSequencerV1 Sequencer, VoiceActivityEffectivePlanV1 Candidate);
}
