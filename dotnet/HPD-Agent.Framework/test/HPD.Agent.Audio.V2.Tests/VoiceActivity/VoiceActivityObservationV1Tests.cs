using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityObservationV1Tests
{
    [Fact]
    public void Dynamic_observer_admission_reserves_aggregate_capacity()
    {
        using var hub = new VoiceActivityObservationHubV1(2, 3);
        Assert.Equal(VoiceActivityObserverAdmissionResultV1.Admitted, hub.TrySubscribe(2, out var first));
        Assert.Equal(VoiceActivityObserverAdmissionResultV1.AggregateCapacityExceeded,
            hub.TrySubscribe(2, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(VoiceActivityObserverAdmissionResultV1.Admitted, hub.TrySubscribe(1, out var second));
        first!.Dispose();
        Assert.Equal(VoiceActivityObserverAdmissionResultV1.Admitted, hub.TrySubscribe(2, out var replacement));
        second!.Dispose();
        replacement!.Dispose();
    }

    [Fact]
    public void Slow_observer_drops_oldest_without_blocking_authority()
    {
        var lifecycle = Lifecycle();
        using var hub = new VoiceActivityObservationHubV1(1, 1);
        hub.TrySubscribe(1, out var subscription);
        var writer = new VoiceActivitySnapshotWriterV1(hub);
        Assert.True(writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Unknown, 0,
            Health(), []));
        Assert.True(writer.TryPublish(1, lifecycle.Current, VoiceActivityPromotionStateV1.Closed, 1,
            Health(), ["source-gap"]));
        Assert.Equal(1UL, hub.Drops);
        Assert.True(subscription!.TryRead(out var latest));
        Assert.Equal(2UL, latest!.ProjectionSequence);
        Assert.Equal(VoiceActivityPromotionStateV1.Closed, latest.PromotionState);
    }

    [Fact]
    public void Exporters_off_and_observers_present_produce_identical_authority_projection()
    {
        var lifecycle = Lifecycle();
        var without = new VoiceActivitySnapshotWriterV1();
        using var hub = new VoiceActivityObservationHubV1(1, 4);
        hub.TrySubscribe(4, out _);
        var with = new VoiceActivitySnapshotWriterV1(hub);
        Assert.True(without.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Open, 3,
            Health(), ["degraded-optional-source"]));
        Assert.True(with.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Open, 3,
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
        var writer = new VoiceActivitySnapshotWriterV1();
        Assert.True(writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Open, 2, Health(), []));
        Assert.False(writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Closed, 3, Health(), []));
        Assert.False(writer.TryPublish(1, lifecycle.Current, VoiceActivityPromotionStateV1.Open, 1, Health(), []));
    }

    [Fact]
    public void Completion_projection_cannot_be_rewritten_by_late_release()
    {
        var lifecycle = Lifecycle();
        var completed = lifecycle.Complete(VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed);
        var writer = new VoiceActivitySnapshotWriterV1();
        Assert.True(writer.TryPublish(0, completed, VoiceActivityPromotionStateV1.Discontinuous, 4,
            new Dictionary<string, VoiceActivitySourceHealthV1> { ["local"] = VoiceActivitySourceHealthV1.Stopped },
            ["completion-release-unconfirmed"]));
        lifecycle.ObserveLateRelease(OperationId.Create(), VoiceActivityReleaseDispositionV1.Confirmed);
        Assert.False(writer.TryPublish(1, lifecycle.Current, VoiceActivityPromotionStateV1.Discontinuous, 4,
            new Dictionary<string, VoiceActivitySourceHealthV1> { ["local"] = VoiceActivitySourceHealthV1.Stopped }, []));
        Assert.Equal(["completion-release-unconfirmed"], writer.Current!.Warnings);
    }

    [Fact]
    public void Snapshot_owns_health_and_warning_collections()
    {
        var lifecycle = Lifecycle();
        var health = Health();
        var warnings = new List<string> { "initial" };
        var writer = new VoiceActivitySnapshotWriterV1();
        writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Unknown, 0, health, warnings);
        health["local"] = VoiceActivitySourceHealthV1.Faulted;
        warnings[0] = "mutated";
        Assert.Equal(VoiceActivitySourceHealthV1.Ready, writer.Current!.SourceHealth["local"]);
        Assert.Equal(["initial"], writer.Current.Warnings);
    }

    private static Dictionary<string, VoiceActivitySourceHealthV1> Health() =>
        new() { ["local"] = VoiceActivitySourceHealthV1.Ready };

    private static VoiceActivityLifecycleV1 Lifecycle()
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
        var plan = Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(VoiceActivityPlanCompilerV1.Compile(
            1, 1, request, [new("local", ActivitySourceKindV1.LocalDetector, capabilities, true,
                ProviderActivityVisibilityV1.AcceptedLocally)])).Plan;
        return new VoiceActivityLifecycleV1(new(RuntimeGenerationId.Create(), LiveSessionId.Create()),
            GraphDirectionV1.IngressForward, plan, new Dictionary<string, ulong> { ["local"] = 1 });
    }
}
