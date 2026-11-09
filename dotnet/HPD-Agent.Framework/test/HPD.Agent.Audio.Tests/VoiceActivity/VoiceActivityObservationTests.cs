using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityObservationTests
{
    [Fact]
    public void Dynamic_observer_admission_reserves_aggregate_capacity()
    {
        using var hub = new VoiceActivityObservationHub(2, 3);
        Assert.Equal(VoiceActivityObserverAdmissionResult.Admitted, hub.TrySubscribe(2, out var first));
        Assert.Equal(VoiceActivityObserverAdmissionResult.AggregateCapacityExceeded,
            hub.TrySubscribe(2, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(VoiceActivityObserverAdmissionResult.Admitted, hub.TrySubscribe(1, out var second));
        first!.Dispose();
        Assert.Equal(VoiceActivityObserverAdmissionResult.Admitted, hub.TrySubscribe(2, out var replacement));
        second!.Dispose();
        replacement!.Dispose();
    }

    [Fact]
    public void Slow_observer_drops_oldest_without_blocking_authority()
    {
        var lifecycle = Lifecycle();
        using var hub = new VoiceActivityObservationHub(1, 1);
        hub.TrySubscribe(1, out var subscription);
        var writer = new VoiceActivitySnapshotWriter(hub);
        Assert.True(writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Unknown, 0,
            Health(), []));
        Assert.True(writer.TryPublish(1, lifecycle.Current, VoiceActivityPromotionState.Closed, 1,
            Health(), ["source-gap"]));
        Assert.Equal(1UL, hub.Drops);
        Assert.True(subscription!.TryRead(out var latest));
        Assert.Equal(2UL, latest!.ProjectionSequence);
        Assert.Equal(VoiceActivityPromotionState.Closed, latest.PromotionState);
    }

    [Fact]
    public void Exporters_off_and_observers_present_produce_identical_authority_projection()
    {
        var lifecycle = Lifecycle();
        var without = new VoiceActivitySnapshotWriter();
        using var hub = new VoiceActivityObservationHub(1, 4);
        hub.TrySubscribe(4, out _);
        var with = new VoiceActivitySnapshotWriter(hub);
        Assert.True(without.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Open, 3,
            Health(), ["degraded-optional-source"]));
        Assert.True(with.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Open, 3,
            Health(), ["degraded-optional-source"]));
        Assert.Equal(without.Current!.ProjectionSequence, with.Current!.ProjectionSequence);
        Assert.Equal(without.Current.Lifecycle.LifecycleRevision, with.Current.Lifecycle.LifecycleRevision);
        Assert.Equal(without.Current.PromotionState, with.Current.PromotionState);
        Assert.Equal(without.Current.LastPromotionSequence, with.Current.LastPromotionSequence);
        Assert.Equal(without.Current.Warnings, with.Current.Warnings);
    }

    [Fact]
    public void Snapshot_writer_rejects_stale_sequence_lifecycle_and_promotion()
    {
        var lifecycle = Lifecycle();
        var writer = new VoiceActivitySnapshotWriter();
        Assert.True(writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Open, 2, Health(), []));
        Assert.False(writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Closed, 3, Health(), []));
        Assert.False(writer.TryPublish(1, lifecycle.Current, VoiceActivityPromotionState.Open, 1, Health(), []));
    }

    [Fact]
    public void Completion_projection_cannot_be_rewritten_by_late_release()
    {
        var lifecycle = Lifecycle();
        var completed = lifecycle.Complete(VoiceActivityReleaseDisposition.ReleaseUnconfirmed);
        var writer = new VoiceActivitySnapshotWriter();
        Assert.True(writer.TryPublish(0, completed, VoiceActivityPromotionState.Discontinuous, 4,
            new Dictionary<string, VoiceActivitySourceHealth> { ["local"] = VoiceActivitySourceHealth.Stopped },
            ["completion-release-unconfirmed"]));
        lifecycle.ObserveLateRelease(OperationId.Create(), VoiceActivityReleaseDisposition.Confirmed);
        Assert.False(writer.TryPublish(1, lifecycle.Current, VoiceActivityPromotionState.Discontinuous, 4,
            new Dictionary<string, VoiceActivitySourceHealth> { ["local"] = VoiceActivitySourceHealth.Stopped }, []));
        Assert.Equal(["completion-release-unconfirmed"], writer.Current!.Warnings);
    }

    [Fact]
    public void Snapshot_owns_health_and_warning_collections()
    {
        var lifecycle = Lifecycle();
        var health = Health();
        var warnings = new List<string> { "initial" };
        var writer = new VoiceActivitySnapshotWriter();
        writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Unknown, 0, health, warnings);
        health["local"] = VoiceActivitySourceHealth.Faulted;
        warnings[0] = "mutated";
        Assert.Equal(VoiceActivitySourceHealth.Ready, writer.Current!.SourceHealth["local"]);
        Assert.Equal(["initial"], writer.Current.Warnings);
    }

    private static Dictionary<string, VoiceActivitySourceHealth> Health() =>
        new() { ["local"] = VoiceActivitySourceHealth.Ready };

    private static VoiceActivityLifecycle Lifecycle()
    {
        var request = new VoiceActivityRequest(VoiceActivityProfile.HpdManaged,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, null,
            [new("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true)],
            ActivityDegradationPolicy.Strict,
            new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        var capabilities = new VoiceActivitySourceCapabilities(VoiceActivityInputOwnership.BorrowedSynchronous,
            [new(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
            new(VoiceActivityMeasurementKind.EngineScore, new BoundedAscii("score"), -1, 1, null),
            VoiceActivitySourceStateModel.Stateless, VoiceActivitySourceConcurrency.Serial,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.ReplacementRequired,
            true, false, 1);
        var plan = Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(VoiceActivityPlanCompiler.Compile(
            1, 1, request, [new("local", ActivitySourceKind.LocalDetector, capabilities, true,
                ProviderActivityVisibility.AcceptedLocally)])).Plan;
        return new VoiceActivityLifecycle(new(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirection.IngressForward, plan, new Dictionary<string, ulong> { ["local"] = 1 });
    }
}
