using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityUpdateTransactionTests
{
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();

    [Fact]
    public void Applied_update_has_a_real_ordered_lifecycle_cut()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Staged,
            fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
                [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
                ProviderActivityVisibility.Acknowledged).Disposition);
        var applied = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Applied, applied.Disposition);
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
            [Field(VoiceActivityUpdateFieldDisposition.RequiresReplacement)], VoiceActivityCurrentSpeechHandling.MarkDiscontinuousAtCut,
            ProviderActivityVisibility.Unknown);
        var result = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.RequiresReplacement, result.Disposition);
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
            [Field(VoiceActivityUpdateFieldDisposition.Applied),
                Field(VoiceActivityUpdateFieldDisposition.RequestedUnconfirmed,
                    VoiceActivityUpdateField.NoiseEnvironment)],
            VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut, ProviderActivityVisibility.NotObservable);
        var result = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.PartiallyApplied, result.Disposition);
        Assert.True(result.Degraded);
        Assert.Equal(VoiceActivityUpdateFieldDisposition.RequestedUnconfirmed,
            result.Fields.Single(field => field.Field == VoiceActivityUpdateField.NoiseEnvironment).Disposition);
        Assert.Equal(2UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Rejected_field_prevents_staging_or_effect()
    {
        var fixture = Fixture();
        var result = fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Rejected)], VoiceActivityCurrentSpeechHandling.ResetAtCut,
            ProviderActivityVisibility.Rejected);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected, result.Disposition);
        Assert.True(result.RolledBack);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Concurrent_second_update_is_superseded_not_last_writer_wins()
    {
        var fixture = Fixture();
        fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        var second = fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Superseded, second.Disposition);
        Assert.All(second.Fields, field =>
            Assert.Equal(VoiceActivityUpdateFieldDisposition.Superseded, field.Disposition));
    }

    [Fact]
    public void Stale_revision_is_rejected_by_fence()
    {
        var fixture = Fixture();
        var result = fixture.Sequencer.Stage(OperationId.Create(), 99, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ResetAtCut,
            ProviderActivityVisibility.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.RejectedByFence, result.Disposition);
        Assert.Equal(result.OldRevision, result.NewRevision);
    }

    [Fact]
    public void Reset_between_stage_and_commit_rejects_by_fence()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        fixture.Lifecycle.Reset(1, "test-reset", Stamp(2));
        var result = fixture.Sequencer.Commit(operation, Stamp(3));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.RejectedByFence, result.Disposition);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Fact]
    public void Stop_between_stage_and_commit_is_completed_before_commit()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        fixture.Lifecycle.Complete(VoiceActivityReleaseDisposition.Confirmed);
        var result = fixture.Sequencer.Commit(operation, Stamp(2));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.CompletedBeforeCommit, result.Disposition);
        Assert.Equal(VoiceActivityLifecycleState.Completed, fixture.Lifecycle.Current.State);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Fact]
    public void Replacement_prepare_between_stage_and_commit_fences_update()
    {
        var fixture = Fixture();
        var update = OperationId.Create();
        fixture.Sequencer.Stage(update, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        var replacement = new VoiceActivityReplacementProof(OperationId.Create(), fixture.Candidate,
            Stamp(2), Stamp(10), VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen,
            true, true, null, null, false);
        Assert.IsType<VoiceActivityPrepareReplacementResult.Prepared>(
            fixture.Lifecycle.PrepareReplacement(1, replacement));
        var result = fixture.Sequencer.Commit(update, Stamp(3));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.RejectedByFence, result.Disposition);
        Assert.Equal(VoiceActivityLifecycleState.ReplacementPrepared, fixture.Lifecycle.Current.State);
        Assert.Equal(1UL, fixture.Lifecycle.Current.Plan.PlanGeneration);
    }

    [Theory]
    [InlineData((int)VoiceActivityUpdateField.Profile)]
    [InlineData((int)VoiceActivityUpdateField.PrefixContext)]
    [InlineData((int)VoiceActivityUpdateField.Sources)]
    [InlineData((int)VoiceActivityUpdateField.Limits)]
    [InlineData((int)VoiceActivityUpdateField.Calibration)]
    [InlineData((int)VoiceActivityUpdateField.Authority)]
    public void Structural_fields_cannot_claim_in_place_application(int fieldValue)
    {
        var fixture = Fixture();
        var field = (VoiceActivityUpdateField)fieldValue;
        var result = fixture.Sequencer.Stage(OperationId.Create(), 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied, field)], VoiceActivityCurrentSpeechHandling.ResetAtCut,
            ProviderActivityVisibility.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected, result.Disposition);
        Assert.Equal("field-requires-replacement", result.Fields.Single().Reason);
    }

    [Fact]
    public void Source_without_sequenced_capability_cannot_claim_applied()
    {
        var current = Plan(1, 1, ActivityResponsiveness.Balanced,
            VoiceActivitySourceControl.Unsupported);
        var lifecycle = new VoiceActivityLifecycle(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirection.IngressForward, current, new Dictionary<string, ulong> { ["local"] = 1 });
        var sequencer = new VoiceActivityUpdateSequencer(lifecycle);
        var result = sequencer.Stage(OperationId.Create(), 1,
            Plan(2, 2, ActivityResponsiveness.Responsive, VoiceActivitySourceControl.Unsupported),
            Stamp(1), [Field(VoiceActivityUpdateFieldDisposition.Applied)], VoiceActivityCurrentSpeechHandling.ResetAtCut,
            ProviderActivityVisibility.Acknowledged);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected, result.Disposition);
        Assert.Equal("sequenced-update-unsupported", result.Fields.Single().Reason);
    }

    [Fact]
    public void Terminal_exact_retry_replays_but_changed_operation_intent_is_rejected()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        var fields = new[] { Field(VoiceActivityUpdateFieldDisposition.Applied) };
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1), fields,
            VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut, ProviderActivityVisibility.Acknowledged);
        var applied = fixture.Sequencer.Commit(operation, Stamp(2));
        var retry = fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(3), fields,
            VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut, ProviderActivityVisibility.Acknowledged);
        Assert.Same(applied, retry);
        var changed = fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(4),
            [Field(VoiceActivityUpdateFieldDisposition.RequiresReplacement)],
            VoiceActivityCurrentSpeechHandling.MarkDiscontinuousAtCut, ProviderActivityVisibility.Unknown);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected, changed.Disposition);
        Assert.Contains("update-operation-contradiction", changed.Warnings);
        Assert.Equal(2UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Commit_cut_cannot_move_before_stage_observation()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(5),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)],
            VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        var result = fixture.Sequencer.Commit(operation, Stamp(4));
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected, result.Disposition);
        Assert.Contains("update-cut-invalid", result.Warnings);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Commit_cut_on_an_incomparable_clock_is_rejected()
    {
        var fixture = Fixture();
        var operation = OperationId.Create();
        fixture.Sequencer.Stage(operation, 1, fixture.Candidate, Stamp(1),
            [Field(VoiceActivityUpdateFieldDisposition.Applied)],
            VoiceActivityCurrentSpeechHandling.ContinueUnderOldUntilCut,
            ProviderActivityVisibility.Acknowledged);
        var incomparable = new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 2);
        var result = fixture.Sequencer.Commit(operation, incomparable);
        Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected, result.Disposition);
        Assert.Contains("update-cut-invalid", result.Warnings);
        Assert.Equal(1UL, fixture.Lifecycle.Current.LifecycleRevision);
    }

    [Fact]
    public void Terminal_retry_history_is_strictly_bounded()
    {
        var lifecycle = new VoiceActivityLifecycle(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirection.IngressForward, Plan(1, 1, ActivityResponsiveness.Balanced),
            new Dictionary<string, ulong> { ["local"] = 1 });
        var sequencer = new VoiceActivityUpdateSequencer(lifecycle, 2);
        var candidate = Plan(2, 2, ActivityResponsiveness.Responsive);
        for (var index = 0; index < 3; index++)
            Assert.Equal(VoiceActivityDetailedUpdateDisposition.Rejected,
                sequencer.Stage(OperationId.Create(), 1, candidate, Stamp((ulong)index + 1),
                    [Field(VoiceActivityUpdateFieldDisposition.Rejected)],
                    VoiceActivityCurrentSpeechHandling.ResetAtCut,
                    ProviderActivityVisibility.Rejected).Disposition);
        Assert.Equal(2, sequencer.TerminalHistoryCount);
    }

    private static UpdateFixture Fixture()
    {
        var lifecycle = new VoiceActivityLifecycle(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirection.IngressForward, Plan(1, 1, ActivityResponsiveness.Balanced),
            new Dictionary<string, ulong> { ["local"] = 1 });
        return new(lifecycle, new VoiceActivityUpdateSequencer(lifecycle),
            Plan(2, 2, ActivityResponsiveness.Responsive));
    }

    private static VoiceActivityUpdateFieldResult Field(VoiceActivityUpdateFieldDisposition disposition,
        VoiceActivityUpdateField field = VoiceActivityUpdateField.Responsiveness) => new(field,
        VoiceActivityUpdateFieldOwner.Source, "local", "responsive",
        disposition == VoiceActivityUpdateFieldDisposition.Applied ? "responsive" : "balanced",
        disposition, disposition == VoiceActivityUpdateFieldDisposition.Applied ? "source-sequenced" : "not-proven");

    private static VoiceActivityEffectivePlan Plan(ulong generation, ulong revision,
        ActivityResponsiveness responsiveness,
        VoiceActivitySourceControl dynamicUpdate = VoiceActivitySourceControl.Sequenced)
    {
        var request = new VoiceActivityRequest(VoiceActivityProfile.HpdManaged, responsiveness,
            VoiceActivityNoiseEnvironment.Variable, VoiceActivitySpeechContinuity.Natural, null,
            [new("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true)],
            ActivityDegradationPolicy.Strict,
            new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var capabilities = new VoiceActivitySourceCapabilities(VoiceActivityInputOwnership.BorrowedSynchronous,
            [new(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
            new(VoiceActivityMeasurementKind.EngineScore, new BoundedAscii("score"), -1, 1, null),
            VoiceActivitySourceStateModel.Stateless, VoiceActivitySourceConcurrency.Serial,
            dynamicUpdate, VoiceActivitySourceControl.Sequenced,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.ReplacementRequired,
            true, false, 1);
        return Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(VoiceActivityPlanCompiler.Compile(
            generation, revision, request, [new("local", ActivitySourceKind.LocalDetector, capabilities, true,
                ProviderActivityVisibility.Acknowledged)])).Plan;
    }

    private static MonotonicStampV1 Stamp(ulong value) => new(Clock, Boot, value);

    private sealed record UpdateFixture(VoiceActivityLifecycle Lifecycle,
        VoiceActivityUpdateSequencer Sequencer, VoiceActivityEffectivePlan Candidate);
}
