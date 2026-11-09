using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityCompositionHostTests
{
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();
    private static readonly GraphGenerationId Graph = GraphGenerationId.Create();

    [Theory]
    [InlineData("microphone", 16_000)]
    [InlineData("webrtc-browser", 48_000)]
    [InlineData("telephony", 8_000)]
    public void Local_ingress_shapes_use_one_vendor_neutral_composition_path(string key, int sampleRate)
    {
        var host = Start(Request(VoiceActivityProfile.HpdManaged,
            Source(key, ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative)),
            [Candidate(key, ActivitySourceKind.LocalDetector, sampleRate)], new Dictionary<string, ulong> { [key] = 1 });

        Assert.Equal(sampleRate, Assert.Single(Assert.Single(host.Support.Candidates).Formats).SampleRate);
        Assert.IsType<VoiceActivityPromotionResult.Applied>(host.Apply(key, 1,
            VoiceActivityPromotionEdge.Observation, Observed(true, 1)));
        Assert.Equal(VoiceActivityPromotionFactKind.Opened, Assert.Single(host.Facts).Kind);
    }

    [Fact]
    public void Provider_native_and_split_sources_retain_requested_effective_truth()
    {
        var provider = Start(Request(VoiceActivityProfile.ProviderManaged,
                Source("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Authoritative)),
            [Candidate("provider", ActivitySourceKind.ProviderNative, 0)],
            new Dictionary<string, ulong> { ["provider"] = 2 });
        Assert.Equal(ProviderActivityVisibility.Acknowledged,
            Assert.Single(provider.Support.Candidates).ProviderVisibility);

        var split = Start(Request(VoiceActivityProfile.HpdManaged,
                Source("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative),
                Source("stt", ActivitySourceKind.SttAdjacent, ActivitySourceRole.Fallback)),
            [Candidate("local", ActivitySourceKind.LocalDetector, 16_000),
             Candidate("stt", ActivitySourceKind.SttAdjacent, 16_000)],
            new Dictionary<string, ulong> { ["local"] = 1 });
        Assert.Equal(["local", "stt"], split.Snapshot.Sources.Select(static source => source.SourceKey).ToArray());
        Assert.Equal(ActivitySourceRole.Fallback, split.Snapshot.Sources[1].Role);
    }

    [Fact]
    public void Fusion_and_manual_ptt_share_the_sole_promoter_path()
    {
        var calibration = Hash256.Compute("shared-calibration"u8);
        var fused = Start(Request(VoiceActivityProfile.Fused,
                Source("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative),
                Source("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Corroborating)),
            [Candidate("local", ActivitySourceKind.LocalDetector, 16_000,
                 VoiceActivityMeasurementKind.CalibratedLikelihood, calibration),
             Candidate("provider", ActivitySourceKind.ProviderNative, 0,
                 VoiceActivityMeasurementKind.CalibratedLikelihood, calibration)],
            new Dictionary<string, ulong> { ["local"] = 1, ["provider"] = 1 });
        Assert.IsType<VoiceActivityPromotionResult.Applied>(fused.Apply("local", 1,
            VoiceActivityPromotionEdge.Observation, Observed(true, 1, calibration)));
        Assert.IsType<VoiceActivityPromotionResult.Applied>(fused.Apply("provider", 1,
            VoiceActivityPromotionEdge.Observation, Observed(true, 2, calibration)));
        Assert.Single(fused.Facts, static fact => fact.Kind == VoiceActivityPromotionFactKind.Opened);

        var manual = Start(Request(VoiceActivityProfile.Manual,
                Source("ptt", ActivitySourceKind.Manual, ActivitySourceRole.Authoritative)),
            [Candidate("ptt", ActivitySourceKind.Manual, 16_000)],
            new Dictionary<string, ulong> { ["ptt"] = 3 });
        Assert.IsType<VoiceActivityPromotionResult.Applied>(manual.Apply("ptt", 1,
            VoiceActivityPromotionEdge.ManualPress, Observed(true, 1)));
        Assert.IsType<VoiceActivityPromotionResult.Applied>(manual.Apply("ptt", 2,
            VoiceActivityPromotionEdge.ManualRelease, Observed(false, 2)));
        Assert.Equal([VoiceActivityPromotionFactKind.Opened, VoiceActivityPromotionFactKind.Closed],
            manual.Facts.Select(static fact => fact.Kind).ToArray());
    }

    [Fact]
    public void Multi_tenant_sessions_do_not_share_promoter_state()
    {
        var request = Request(VoiceActivityProfile.HpdManaged,
            Source("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative));
        var candidates = new[] { Candidate("local", ActivitySourceKind.LocalDetector, 16_000) };
        var first = Start(request, candidates, new Dictionary<string, ulong> { ["local"] = 1 }, "tenant-a");
        var second = Start(request, candidates, new Dictionary<string, ulong> { ["local"] = 1 }, "tenant-b");

        Assert.IsType<VoiceActivityPromotionResult.Applied>(first.Apply("local", 1,
            VoiceActivityPromotionEdge.Observation, Observed(true, 1)));
        Assert.Equal(VoiceActivityPromotionState.Open, first.State);
        Assert.Equal(VoiceActivityPromotionState.Unknown, second.State);
        Assert.Empty(second.Facts);
        Assert.NotEqual(first.Support.Identity.Session, second.Support.Identity.Session);
        Assert.NotEqual(first.Support.Identity.TenantKey, second.Support.Identity.TenantKey);
    }

    [Fact]
    public void Finite_inspection_compiles_the_same_snapshot_without_creating_a_live_host()
    {
        var identity = Identity("finite-tenant");
        var request = Request(VoiceActivityProfile.HpdManaged,
            Source("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative));
        var candidates = new[] { Candidate("local", ActivitySourceKind.LocalDetector, 16_000) };

        var finite = VoiceActivityCompositionHost.InspectFinite(1, 1, identity, request, candidates);
        var live = Assert.IsType<VoiceActivityCompositionPreparation.Prepared>(
            VoiceActivityCompositionHost.Start(1, 1, identity, GraphDirection.IngressForward,
                request, candidates, new Dictionary<string, ulong> { ["local"] = 1 })).Host;

        Assert.NotNull(finite.Effective);
        Assert.Equal(live.Snapshot.PlanGeneration, finite.Effective.PlanGeneration);
        Assert.Equal(live.Snapshot.ConfigRevision, finite.Effective.ConfigRevision);
        Assert.Equal(live.Snapshot.RequestedProfile, finite.Effective.RequestedProfile);
        Assert.Equal(live.Snapshot.Health, finite.Effective.Health);
        Assert.Equal(live.Snapshot.Sources, finite.Effective.Sources);
        Assert.Equal(live.Snapshot.RequestedEffectiveDifferences, finite.Effective.RequestedEffectiveDifferences);
        Assert.Null(finite.SafeCode);
        Assert.Empty(live.Facts);
    }

    [Theory]
    [InlineData(ProviderActivityVisibility.Unknown)]
    [InlineData(ProviderActivityVisibility.NotObservable)]
    public void Support_bundle_preserves_provider_visibility_rejection_without_inventing_events(
        ProviderActivityVisibility visibility)
    {
        var identity = Identity("diagnostic-tenant");
        var request = Request(VoiceActivityProfile.ProviderManaged,
            Source("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Authoritative));
        var candidate = Candidate("provider", ActivitySourceKind.ProviderNative, 0, visibility: visibility);

        var rejected = Assert.IsType<VoiceActivityCompositionPreparation.Rejected>(
            VoiceActivityCompositionHost.Start(1, 1, identity, GraphDirection.IngressForward,
                request, [candidate], new Dictionary<string, ulong> { ["provider"] = 1 }));

        Assert.Equal("provider-visibility-insufficient", rejected.Support.SafeCode);
        Assert.Equal(visibility, Assert.Single(rejected.Support.Candidates).ProviderVisibility);
        Assert.Contains($"source:provider:ProviderNative:True:{visibility}", rejected.Support.Diagnostics);
    }

    [Fact]
    public void Provider_reconnect_advances_source_generation_without_late_callback_cross_talk()
    {
        var identity = Identity("provider-tenant");
        var request = Request(VoiceActivityProfile.ProviderManaged,
            Source("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Authoritative));
        var candidate = Candidate("provider", ActivitySourceKind.ProviderNative, 0);
        var first = Assert.IsType<VoiceActivityCompositionPreparation.Prepared>(
            VoiceActivityCompositionHost.Start(1, 1, identity, GraphDirection.IngressForward,
                request, [candidate], new Dictionary<string, ulong> { ["provider"] = 1 })).Host;
        Assert.IsType<VoiceActivityPromotionResult.Applied>(first.Apply("provider", 1,
            VoiceActivityPromotionEdge.Observation,
            new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.ProviderNotObservable)));
        Assert.Equal(VoiceActivityPromotionFactKind.Unobservable, Assert.Single(first.Facts).Kind);

        var replacement = Assert.IsType<VoiceActivityCompositionPreparation.Prepared>(
            VoiceActivityCompositionHost.Start(2, 2, identity, GraphDirection.IngressForward,
                request, [candidate], new Dictionary<string, ulong> { ["provider"] = 2 })).Host;
        Assert.IsType<VoiceActivityPromotionResult.Applied>(replacement.Apply("provider", 1,
            VoiceActivityPromotionEdge.Observation, ProviderObserved(true, 1)));
        Assert.Equal(VoiceActivityPromotionState.Open, replacement.State);

        Assert.IsType<VoiceActivityPromotionResult.Applied>(first.Apply("provider", 2,
            VoiceActivityPromotionEdge.Observation, ProviderObserved(true, 2)));
        Assert.Single(replacement.Facts);
        Assert.Equal(2UL, replacement.Snapshot.PlanGeneration);
        Assert.Equal(2UL, replacement.Facts[0].PlanGeneration);
    }

    [Fact]
    public void Explicit_transport_discontinuity_is_factual_and_recovery_does_not_synthesize_close()
    {
        var host = Start(Request(VoiceActivityProfile.HpdManaged,
                Source("webrtc", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative)),
            [Candidate("webrtc", ActivitySourceKind.LocalDetector, 48_000)],
            new Dictionary<string, ulong> { ["webrtc"] = 1 });
        Assert.IsType<VoiceActivityPromotionResult.Applied>(host.Apply("webrtc", 1,
            VoiceActivityPromotionEdge.Observation, Observed(true, 1)));
        Assert.IsType<VoiceActivityPromotionResult.Applied>(host.Apply("webrtc", 2,
            VoiceActivityPromotionEdge.Discontinuity, null));
        Assert.Equal(VoiceActivityPromotionFactKind.Discontinuous, host.Facts[1].Kind);
        Assert.DoesNotContain(host.Facts, static fact => fact.Kind == VoiceActivityPromotionFactKind.Closed);

        Assert.IsType<VoiceActivityPromotionResult.Applied>(host.Apply("webrtc", 3,
            VoiceActivityPromotionEdge.Observation, Observed(true, 3)));
        Assert.Equal(VoiceActivityPromotionFactKind.Opened, host.Facts[^1].Kind);
    }

    [Fact]
    public void Repeated_finite_inspection_is_inert_while_live_activity_remains_open()
    {
        var identity = Identity("coexistence-tenant");
        var request = Request(VoiceActivityProfile.HpdManaged,
            Source("microphone", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative));
        var candidates = new[] { Candidate("microphone", ActivitySourceKind.LocalDetector, 16_000) };
        var live = Assert.IsType<VoiceActivityCompositionPreparation.Prepared>(
            VoiceActivityCompositionHost.Start(1, 1, identity, GraphDirection.IngressForward,
                request, candidates, new Dictionary<string, ulong> { ["microphone"] = 1 })).Host;
        Assert.IsType<VoiceActivityPromotionResult.Applied>(live.Apply("microphone", 1,
            VoiceActivityPromotionEdge.Observation, Observed(true, 1)));

        for (var index = 0; index < 100; index++)
        {
            var finite = VoiceActivityCompositionHost.InspectFinite(1, 1, identity, request, candidates);
            Assert.Equal(VoiceActivityHealthState.Ready, finite.Effective?.Health);
            Assert.Null(finite.SafeCode);
        }

        Assert.Equal(VoiceActivityPromotionState.Open, live.State);
        Assert.Single(live.Facts);
        Assert.Equal(VoiceActivityPromotionFactKind.Opened, live.Facts[0].Kind);
    }

    private static VoiceActivityCompositionHost Start(VoiceActivityRequest request,
        IReadOnlyList<VoiceActivitySourceCandidate> candidates, IReadOnlyDictionary<string, ulong> generations,
        string tenant = "tenant") => Assert.IsType<VoiceActivityCompositionPreparation.Prepared>(
        VoiceActivityCompositionHost.Start(1, 1, Identity(tenant), GraphDirection.IngressForward,
            request, candidates, generations)).Host;

    private static VoiceActivityCompositionIdentity Identity(string tenant) => new(tenant,
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()));

    private static VoiceActivityRequest Request(VoiceActivityProfile profile,
        params ActivitySourceRequest[] sources) => new(profile, ActivityResponsiveness.Responsive,
        VoiceActivityNoiseEnvironment.Variable, VoiceActivitySpeechContinuity.Natural, null, sources,
        ActivityDegradationPolicy.Strict,
        new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static ActivitySourceRequest Source(string key, ActivitySourceKind kind,
        ActivitySourceRole role) => new(key, kind, role, true);

    private static VoiceActivitySourceCandidate Candidate(string key, ActivitySourceKind kind, int sampleRate,
        VoiceActivityMeasurementKind? measurement = null, Hash256? calibration = null,
        ProviderActivityVisibility? visibility = null)
    {
        var opaque = kind == ActivitySourceKind.ProviderNative;
        var measurementKind = measurement ?? kind switch
        {
            ActivitySourceKind.Manual => VoiceActivityMeasurementKind.PostProcessedState,
            ActivitySourceKind.SttAdjacent => VoiceActivityMeasurementKind.ProviderOpaqueCategory,
            _ => VoiceActivityMeasurementKind.EngineScore,
        };
        return new VoiceActivitySourceCandidate(key, kind, new VoiceActivitySourceCapabilities(
            opaque ? VoiceActivityInputOwnership.ProviderOpaque : VoiceActivityInputOwnership.BorrowedSynchronous,
            [opaque ? new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0) :
                new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, sampleRate, 1)],
            new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 1),
            new VoiceActivityMeasurementDescriptor(measurementKind, new BoundedAscii("composition"), -1, 1,
                measurementKind == VoiceActivityMeasurementKind.CalibratedLikelihood ? calibration : null),
            opaque ? VoiceActivitySourceStateModel.ProviderOpaque : VoiceActivitySourceStateModel.Stateless,
            opaque ? VoiceActivitySourceConcurrency.ProviderManaged : VoiceActivitySourceConcurrency.Serial,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
            opaque ? VoiceActivitySourceControl.Sequenced : VoiceActivitySourceControl.Unsupported,
            VoiceActivitySourceControl.ReplacementRequired, true, false, opaque ? 4 : 1), true,
            visibility ?? (opaque ? ProviderActivityVisibility.Acknowledged :
                ProviderActivityVisibility.AcceptedLocally));
    }

    private static VoiceActivitySourceOutcome.Observed Observed(bool active, ulong sequence,
        Hash256? calibration = null)
    {
        var descriptor = new VoiceActivityMeasurementDescriptor(
            calibration.HasValue ? VoiceActivityMeasurementKind.CalibratedLikelihood :
                VoiceActivityMeasurementKind.BinaryDecision,
            new BoundedAscii("composition"), -1, 1, calibration);
        return new VoiceActivitySourceOutcome.Observed(
            calibration.HasValue ? new VoiceActivityMeasurement.Numeric(active ? 1 : -1) :
                new VoiceActivityMeasurement.Binary(active), descriptor,
            new VoiceActivityMediaExtent(Graph, (long)sequence * 1_000, ((long)sequence + 1) * 1_000, true),
            sequence, new MonotonicStampV1(Clock, Boot, sequence),
            new MonotonicStampV1(Clock, Boot, sequence));
    }

    private static VoiceActivitySourceOutcome.Observed ProviderObserved(bool active, ulong sequence)
    {
        var descriptor = new VoiceActivityMeasurementDescriptor(
            VoiceActivityMeasurementKind.ProviderOpaqueCategory, new BoundedAscii("provider-state"), -1, 1, null);
        return new VoiceActivitySourceOutcome.Observed(
            new VoiceActivityMeasurement.Category(new BoundedAscii(active ? "active" : "inactive")), descriptor,
            new VoiceActivityMediaExtent(Graph, (long)sequence * 1_000, ((long)sequence + 1) * 1_000, true),
            sequence, new MonotonicStampV1(Clock, Boot, sequence),
            new MonotonicStampV1(Clock, Boot, sequence));
    }
}
