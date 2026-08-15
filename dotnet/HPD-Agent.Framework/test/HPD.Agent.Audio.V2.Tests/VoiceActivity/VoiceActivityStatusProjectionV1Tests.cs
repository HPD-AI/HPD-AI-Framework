using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityStatusProjectionV1Tests
{
    [Fact]
    public void Provider_status_progression_is_sequenced_and_truthful()
    {
        var tracker = Tracker();
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Applied>(
            tracker.Apply("local", 1, 1, ProviderActivityVisibilityV1.Translated));
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Applied>(
            tracker.Apply("local", 1, 2, ProviderActivityVisibilityV1.AcceptedLocally));
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Applied>(
            tracker.Apply("local", 1, 3, ProviderActivityVisibilityV1.Acknowledged));
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Applied>(
            tracker.Apply("local", 1, 4, ProviderActivityVisibilityV1.ObservedConsistent));
        Assert.Equal(ProviderActivityVisibilityV1.ObservedConsistent, tracker.Snapshot["local"]);
    }

    [Fact]
    public void Time_or_sequence_cannot_upgrade_not_observable_into_proof()
    {
        var tracker = Tracker();
        tracker.Apply("local", 1, 1, ProviderActivityVisibilityV1.NotObservable);
        var rejected = Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Rejected>(
            tracker.Apply("local", 1, 999, ProviderActivityVisibilityV1.ObservedConsistent));
        Assert.Equal("provider-status-transition-invalid", rejected.SafeCode);
        Assert.Equal(ProviderActivityVisibilityV1.NotObservable, tracker.Snapshot["local"]);
    }

    [Fact]
    public void Reconnect_requires_a_new_generation_starting_requested()
    {
        var tracker = Tracker();
        tracker.Apply("local", 1, 1, ProviderActivityVisibilityV1.ReconnectRequired);
        var rejected = Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Rejected>(
            tracker.Apply("local", 2, 1, ProviderActivityVisibilityV1.Acknowledged));
        Assert.Equal("provider-generation-must-restart-requested", rejected.SafeCode);
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Applied>(
            tracker.Apply("local", 2, 1, ProviderActivityVisibilityV1.Requested));
    }

    [Fact]
    public void Duplicate_stale_and_contradictory_statuses_are_distinct()
    {
        var tracker = Tracker();
        tracker.Apply("local", 1, 2, ProviderActivityVisibilityV1.Translated);
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Duplicate>(
            tracker.Apply("local", 1, 2, ProviderActivityVisibilityV1.Translated));
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Rejected>(
            tracker.Apply("local", 1, 2, ProviderActivityVisibilityV1.Unknown));
        Assert.IsType<VoiceActivityProviderStatusUpdateResultV1.Stale>(
            tracker.Apply("local", 1, 1, ProviderActivityVisibilityV1.Translated));
    }

    [Fact]
    public void Diagnostic_adapter_exports_a_bounded_projection()
    {
        var lifecycle = Lifecycle();
        using var hub = new VoiceActivityObservationHubV1(1, 4);
        hub.TrySubscribe(4, out var subscription);
        var writer = new VoiceActivitySnapshotWriterV1(hub);
        for (ulong sequence = 0; sequence < 4; sequence++)
            Assert.True(writer.TryPublish(sequence, lifecycle.Current, VoiceActivityPromotionStateV1.Open,
                sequence, Health(), []));
        var sink = new RecordingSink();
        var adapter = new VoiceActivityDiagnosticAdapterV1(subscription!, sink);
        Assert.Equal(2, adapter.Drain(2));
        Assert.Equal(2UL, adapter.Exported);
        Assert.Equal(2, sink.Items.Count);
        Assert.Equal(2, adapter.Drain(2));
        Assert.Equal(4UL, adapter.Exported);
    }

    [Fact]
    public void Rejecting_and_throwing_sinks_cannot_feed_back_into_authority()
    {
        var lifecycle = Lifecycle();
        using var rejectedHub = new VoiceActivityObservationHubV1(1, 1);
        rejectedHub.TrySubscribe(1, out var rejectedSubscription);
        var rejectedWriter = new VoiceActivitySnapshotWriterV1(rejectedHub);
        rejectedWriter.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Unknown, 0, Health(), []);
        var rejectedAdapter = new VoiceActivityDiagnosticAdapterV1(rejectedSubscription!, new RejectingSink());
        Assert.Equal(1, rejectedAdapter.Drain(1));
        Assert.Equal(1UL, rejectedAdapter.Rejected);
        Assert.NotNull(rejectedWriter.Current);

        using var faultedHub = new VoiceActivityObservationHubV1(1, 1);
        faultedHub.TrySubscribe(1, out var faultedSubscription);
        var faultedWriter = new VoiceActivitySnapshotWriterV1(faultedHub);
        faultedWriter.TryPublish(0, lifecycle.Current, VoiceActivityPromotionStateV1.Unknown, 0, Health(), []);
        var faultedAdapter = new VoiceActivityDiagnosticAdapterV1(faultedSubscription!, new ThrowingSink());
        Assert.Equal(1, faultedAdapter.Drain(1));
        Assert.Equal(1UL, faultedAdapter.Faulted);
        Assert.NotNull(faultedWriter.Current);
    }

    private static VoiceActivityProviderStatusTrackerV1 Tracker() =>
        new(new Dictionary<string, ulong> { ["local"] = 1 });

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

    private sealed class RecordingSink : IVoiceActivityDiagnosticSinkV1
    {
        internal List<VoiceActivityDiagnosticProjectionV1> Items { get; } = [];
        public bool TryWrite(VoiceActivityDiagnosticProjectionV1 projection)
        {
            Items.Add(projection);
            return true;
        }
    }

    private sealed class RejectingSink : IVoiceActivityDiagnosticSinkV1
    {
        public bool TryWrite(VoiceActivityDiagnosticProjectionV1 projection) => false;
    }

    private sealed class ThrowingSink : IVoiceActivityDiagnosticSinkV1
    {
        public bool TryWrite(VoiceActivityDiagnosticProjectionV1 projection) =>
            throw new InvalidOperationException("diagnostic-sink-failed");
    }
}
