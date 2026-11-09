using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityStatusProjectionTests
{
    [Fact]
    public void Provider_status_progression_is_sequenced_and_truthful()
    {
        var tracker = Tracker();
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Applied>(
            tracker.Apply("local", 1, 1, ProviderActivityVisibility.Translated));
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Applied>(
            tracker.Apply("local", 1, 2, ProviderActivityVisibility.AcceptedLocally));
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Applied>(
            tracker.Apply("local", 1, 3, ProviderActivityVisibility.Acknowledged));
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Applied>(
            tracker.Apply("local", 1, 4, ProviderActivityVisibility.ObservedConsistent));
        Assert.Equal(ProviderActivityVisibility.ObservedConsistent, tracker.Snapshot["local"]);
    }

    [Fact]
    public void Time_or_sequence_cannot_upgrade_not_observable_into_proof()
    {
        var tracker = Tracker();
        tracker.Apply("local", 1, 1, ProviderActivityVisibility.NotObservable);
        var rejected = Assert.IsType<VoiceActivityProviderStatusUpdateResult.Rejected>(
            tracker.Apply("local", 1, 999, ProviderActivityVisibility.ObservedConsistent));
        Assert.Equal("provider-status-transition-invalid", rejected.SafeCode);
        Assert.Equal(ProviderActivityVisibility.NotObservable, tracker.Snapshot["local"]);
    }

    [Fact]
    public void Reconnect_requires_a_new_generation_starting_requested()
    {
        var tracker = Tracker();
        tracker.Apply("local", 1, 1, ProviderActivityVisibility.ReconnectRequired);
        var rejected = Assert.IsType<VoiceActivityProviderStatusUpdateResult.Rejected>(
            tracker.Apply("local", 2, 1, ProviderActivityVisibility.Acknowledged));
        Assert.Equal("provider-generation-must-restart-requested", rejected.SafeCode);
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Applied>(
            tracker.Apply("local", 2, 1, ProviderActivityVisibility.Requested));
    }

    [Fact]
    public void Duplicate_stale_and_contradictory_statuses_are_distinct()
    {
        var tracker = Tracker();
        tracker.Apply("local", 1, 2, ProviderActivityVisibility.Translated);
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Duplicate>(
            tracker.Apply("local", 1, 2, ProviderActivityVisibility.Translated));
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Rejected>(
            tracker.Apply("local", 1, 2, ProviderActivityVisibility.Unknown));
        Assert.IsType<VoiceActivityProviderStatusUpdateResult.Stale>(
            tracker.Apply("local", 1, 1, ProviderActivityVisibility.Translated));
    }

    [Fact]
    public void Diagnostic_adapter_exports_a_bounded_projection()
    {
        var lifecycle = Lifecycle();
        using var hub = new VoiceActivityObservationHub(1, 4);
        hub.TrySubscribe(4, out var subscription);
        var writer = new VoiceActivitySnapshotWriter(hub);
        for (ulong sequence = 0; sequence < 4; sequence++)
            Assert.True(writer.TryPublish(sequence, lifecycle.Current, VoiceActivityPromotionState.Open,
                sequence, Health(), []));
        var sink = new RecordingSink();
        var adapter = new VoiceActivityDiagnosticAdapter(subscription!, sink);
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
        using var rejectedHub = new VoiceActivityObservationHub(1, 1);
        rejectedHub.TrySubscribe(1, out var rejectedSubscription);
        var rejectedWriter = new VoiceActivitySnapshotWriter(rejectedHub);
        rejectedWriter.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Unknown, 0, Health(), []);
        var rejectedAdapter = new VoiceActivityDiagnosticAdapter(rejectedSubscription!, new RejectingSink());
        Assert.Equal(1, rejectedAdapter.Drain(1));
        Assert.Equal(1UL, rejectedAdapter.Rejected);
        Assert.NotNull(rejectedWriter.Current);

        using var faultedHub = new VoiceActivityObservationHub(1, 1);
        faultedHub.TrySubscribe(1, out var faultedSubscription);
        var faultedWriter = new VoiceActivitySnapshotWriter(faultedHub);
        faultedWriter.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Unknown, 0, Health(), []);
        var faultedAdapter = new VoiceActivityDiagnosticAdapter(faultedSubscription!, new ThrowingSink());
        Assert.Equal(1, faultedAdapter.Drain(1));
        Assert.Equal(1UL, faultedAdapter.Faulted);
        Assert.NotNull(faultedWriter.Current);
    }

    [Fact]
    public async Task Hpd_events_projection_uses_bounded_streaming_diagnostic_delivery()
    {
        using var events = new EventCoordinator();
        await using var inbox = events.CreateInbox<VoiceActivityDiagnosticEvent>(
            EventInboxOptions.LossyTelemetry(2));
        var lifecycle = Lifecycle();
        using var hub = new VoiceActivityObservationHub(1, 1);
        hub.TrySubscribe(1, out var subscription);
        var writer = new VoiceActivitySnapshotWriter(hub);
        writer.TryPublish(0, lifecycle.Current, VoiceActivityPromotionState.Open, 1, Health(), ["degraded"]);
        var adapter = new VoiceActivityDiagnosticAdapter(subscription!,
            new VoiceActivityHpdEventDiagnosticSink(events));
        Assert.Equal(1, adapter.Drain(1));
        var emitted = await inbox.Reader.ReadAsync();
        Assert.Equal(EventChannel.Streaming, emitted.Channel);
        Assert.Equal(EventKind.Diagnostic, emitted.Kind);
        Assert.Equal(EventDirection.Upstream, emitted.Direction);
        Assert.Equal(1, emitted.Projection.WarningCount);
        Assert.Equal(1UL, adapter.Exported);
    }

    private static VoiceActivityProviderStatusTracker Tracker() =>
        new(new Dictionary<string, ulong> { ["local"] = 1 });

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

    private sealed class RecordingSink : IVoiceActivityDiagnosticSink
    {
        internal List<VoiceActivityDiagnosticProjection> Items { get; } = [];
        public bool TryWrite(VoiceActivityDiagnosticProjection projection)
        {
            Items.Add(projection);
            return true;
        }
    }

    private sealed class RejectingSink : IVoiceActivityDiagnosticSink
    {
        public bool TryWrite(VoiceActivityDiagnosticProjection projection) => false;
    }

    private sealed class ThrowingSink : IVoiceActivityDiagnosticSink
    {
        public bool TryWrite(VoiceActivityDiagnosticProjection projection) =>
            throw new InvalidOperationException("diagnostic-sink-failed");
    }
}
