using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityCompositionHostV1Tests
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
        var host = Start(Request(VoiceActivityProfileV1.HpdManaged,
            Source(key, ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative)),
            [Candidate(key, ActivitySourceKindV1.LocalDetector, sampleRate)], new Dictionary<string, ulong> { [key] = 1 });

        Assert.Equal(sampleRate, Assert.Single(Assert.Single(host.Support.Candidates).Formats).SampleRate);
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(host.Apply(key, 1,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 1)));
        Assert.Equal(VoiceActivityPromotionFactKindV1.Opened, Assert.Single(host.Facts).Kind);
    }

    [Fact]
    public void Provider_native_and_split_sources_retain_requested_effective_truth()
    {
        var provider = Start(Request(VoiceActivityProfileV1.ProviderManaged,
                Source("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Authoritative)),
            [Candidate("provider", ActivitySourceKindV1.ProviderNative, 0)],
            new Dictionary<string, ulong> { ["provider"] = 2 });
        Assert.Equal(ProviderActivityVisibilityV1.Acknowledged,
            Assert.Single(provider.Support.Candidates).ProviderVisibility);

        var split = Start(Request(VoiceActivityProfileV1.HpdManaged,
                Source("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative),
                Source("stt", ActivitySourceKindV1.SttAdjacent, ActivitySourceRoleV1.Fallback)),
            [Candidate("local", ActivitySourceKindV1.LocalDetector, 16_000),
             Candidate("stt", ActivitySourceKindV1.SttAdjacent, 16_000)],
            new Dictionary<string, ulong> { ["local"] = 1 });
        Assert.Equal(["local", "stt"], split.Snapshot.Sources.Select(static source => source.SourceKey).ToArray());
        Assert.Equal(ActivitySourceRoleV1.Fallback, split.Snapshot.Sources[1].Role);
    }

    [Fact]
    public void Fusion_and_manual_ptt_share_the_sole_promoter_path()
    {
        var calibration = Hash256.Compute("shared-calibration"u8);
        var fused = Start(Request(VoiceActivityProfileV1.Fused,
                Source("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative),
                Source("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Corroborating)),
            [Candidate("local", ActivitySourceKindV1.LocalDetector, 16_000,
                 VoiceActivityMeasurementKindV1.CalibratedLikelihood, calibration),
             Candidate("provider", ActivitySourceKindV1.ProviderNative, 0,
                 VoiceActivityMeasurementKindV1.CalibratedLikelihood, calibration)],
            new Dictionary<string, ulong> { ["local"] = 1, ["provider"] = 1 });
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(fused.Apply("local", 1,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 1, calibration)));
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(fused.Apply("provider", 1,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 2, calibration)));
        Assert.Single(fused.Facts, static fact => fact.Kind == VoiceActivityPromotionFactKindV1.Opened);

        var manual = Start(Request(VoiceActivityProfileV1.Manual,
                Source("ptt", ActivitySourceKindV1.Manual, ActivitySourceRoleV1.Authoritative)),
            [Candidate("ptt", ActivitySourceKindV1.Manual, 16_000)],
            new Dictionary<string, ulong> { ["ptt"] = 3 });
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(manual.Apply("ptt", 1,
            VoiceActivityPromotionEdgeV1.ManualPress, Observed(true, 1)));
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(manual.Apply("ptt", 2,
            VoiceActivityPromotionEdgeV1.ManualRelease, Observed(false, 2)));
        Assert.Equal([VoiceActivityPromotionFactKindV1.Opened, VoiceActivityPromotionFactKindV1.Closed],
            manual.Facts.Select(static fact => fact.Kind).ToArray());
    }

    [Fact]
    public void Multi_tenant_sessions_do_not_share_promoter_state()
    {
        var request = Request(VoiceActivityProfileV1.HpdManaged,
            Source("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative));
        var candidates = new[] { Candidate("local", ActivitySourceKindV1.LocalDetector, 16_000) };
        var first = Start(request, candidates, new Dictionary<string, ulong> { ["local"] = 1 }, "tenant-a");
        var second = Start(request, candidates, new Dictionary<string, ulong> { ["local"] = 1 }, "tenant-b");

        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(first.Apply("local", 1,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 1)));
        Assert.Equal(VoiceActivityPromotionStateV1.Open, first.State);
        Assert.Equal(VoiceActivityPromotionStateV1.Unknown, second.State);
        Assert.Empty(second.Facts);
        Assert.NotEqual(first.Support.Identity.Session, second.Support.Identity.Session);
        Assert.NotEqual(first.Support.Identity.TenantKey, second.Support.Identity.TenantKey);
    }

    [Fact]
    public void Finite_inspection_compiles_the_same_snapshot_without_creating_a_live_host()
    {
        var identity = Identity("finite-tenant");
        var request = Request(VoiceActivityProfileV1.HpdManaged,
            Source("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative));
        var candidates = new[] { Candidate("local", ActivitySourceKindV1.LocalDetector, 16_000) };

        var finite = VoiceActivityCompositionHostV1.InspectFinite(1, 1, identity, request, candidates);
        var live = Assert.IsType<VoiceActivityCompositionPreparationV1.Prepared>(
            VoiceActivityCompositionHostV1.Start(1, 1, identity, GraphDirectionV1.IngressForward,
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
    [InlineData(ProviderActivityVisibilityV1.Unknown)]
    [InlineData(ProviderActivityVisibilityV1.NotObservable)]
    public void Support_bundle_preserves_provider_visibility_rejection_without_inventing_events(
        ProviderActivityVisibilityV1 visibility)
    {
        var identity = Identity("diagnostic-tenant");
        var request = Request(VoiceActivityProfileV1.ProviderManaged,
            Source("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Authoritative));
        var candidate = Candidate("provider", ActivitySourceKindV1.ProviderNative, 0, visibility: visibility);

        var rejected = Assert.IsType<VoiceActivityCompositionPreparationV1.Rejected>(
            VoiceActivityCompositionHostV1.Start(1, 1, identity, GraphDirectionV1.IngressForward,
                request, [candidate], new Dictionary<string, ulong> { ["provider"] = 1 }));

        Assert.Equal("provider-visibility-insufficient", rejected.Support.SafeCode);
        Assert.Equal(visibility, Assert.Single(rejected.Support.Candidates).ProviderVisibility);
        Assert.Contains($"source:provider:ProviderNative:True:{visibility}", rejected.Support.Diagnostics);
    }

    [Fact]
    public void Provider_reconnect_advances_source_generation_without_late_callback_cross_talk()
    {
        var identity = Identity("provider-tenant");
        var request = Request(VoiceActivityProfileV1.ProviderManaged,
            Source("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Authoritative));
        var candidate = Candidate("provider", ActivitySourceKindV1.ProviderNative, 0);
        var first = Assert.IsType<VoiceActivityCompositionPreparationV1.Prepared>(
            VoiceActivityCompositionHostV1.Start(1, 1, identity, GraphDirectionV1.IngressForward,
                request, [candidate], new Dictionary<string, ulong> { ["provider"] = 1 })).Host;
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(first.Apply("provider", 1,
            VoiceActivityPromotionEdgeV1.Observation,
            new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.ProviderNotObservable)));
        Assert.Equal(VoiceActivityPromotionFactKindV1.Unobservable, Assert.Single(first.Facts).Kind);

        var replacement = Assert.IsType<VoiceActivityCompositionPreparationV1.Prepared>(
            VoiceActivityCompositionHostV1.Start(2, 2, identity, GraphDirectionV1.IngressForward,
                request, [candidate], new Dictionary<string, ulong> { ["provider"] = 2 })).Host;
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(replacement.Apply("provider", 1,
            VoiceActivityPromotionEdgeV1.Observation, ProviderObserved(true, 1)));
        Assert.Equal(VoiceActivityPromotionStateV1.Open, replacement.State);

        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(first.Apply("provider", 2,
            VoiceActivityPromotionEdgeV1.Observation, ProviderObserved(true, 2)));
        Assert.Single(replacement.Facts);
        Assert.Equal(2UL, replacement.Snapshot.PlanGeneration);
        Assert.Equal(2UL, replacement.Facts[0].PlanGeneration);
    }

    [Fact]
    public void Explicit_transport_discontinuity_is_factual_and_recovery_does_not_synthesize_close()
    {
        var host = Start(Request(VoiceActivityProfileV1.HpdManaged,
                Source("webrtc", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative)),
            [Candidate("webrtc", ActivitySourceKindV1.LocalDetector, 48_000)],
            new Dictionary<string, ulong> { ["webrtc"] = 1 });
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(host.Apply("webrtc", 1,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 1)));
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(host.Apply("webrtc", 2,
            VoiceActivityPromotionEdgeV1.Discontinuity, null));
        Assert.Equal(VoiceActivityPromotionFactKindV1.Discontinuous, host.Facts[1].Kind);
        Assert.DoesNotContain(host.Facts, static fact => fact.Kind == VoiceActivityPromotionFactKindV1.Closed);

        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(host.Apply("webrtc", 3,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 3)));
        Assert.Equal(VoiceActivityPromotionFactKindV1.Opened, host.Facts[^1].Kind);
    }

    [Fact]
    public void Repeated_finite_inspection_is_inert_while_live_activity_remains_open()
    {
        var identity = Identity("coexistence-tenant");
        var request = Request(VoiceActivityProfileV1.HpdManaged,
            Source("microphone", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative));
        var candidates = new[] { Candidate("microphone", ActivitySourceKindV1.LocalDetector, 16_000) };
        var live = Assert.IsType<VoiceActivityCompositionPreparationV1.Prepared>(
            VoiceActivityCompositionHostV1.Start(1, 1, identity, GraphDirectionV1.IngressForward,
                request, candidates, new Dictionary<string, ulong> { ["microphone"] = 1 })).Host;
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(live.Apply("microphone", 1,
            VoiceActivityPromotionEdgeV1.Observation, Observed(true, 1)));

        for (var index = 0; index < 100; index++)
        {
            var finite = VoiceActivityCompositionHostV1.InspectFinite(1, 1, identity, request, candidates);
            Assert.Equal(VoiceActivityHealthStateV1.Ready, finite.Effective?.Health);
            Assert.Null(finite.SafeCode);
        }

        Assert.Equal(VoiceActivityPromotionStateV1.Open, live.State);
        Assert.Single(live.Facts);
        Assert.Equal(VoiceActivityPromotionFactKindV1.Opened, live.Facts[0].Kind);
    }

    private static VoiceActivityCompositionHostV1 Start(VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivitySourceCandidateV1> candidates, IReadOnlyDictionary<string, ulong> generations,
        string tenant = "tenant") => Assert.IsType<VoiceActivityCompositionPreparationV1.Prepared>(
        VoiceActivityCompositionHostV1.Start(1, 1, Identity(tenant), GraphDirectionV1.IngressForward,
            request, candidates, generations)).Host;

    private static VoiceActivityCompositionIdentityV1 Identity(string tenant) => new(tenant,
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()));

    private static VoiceActivityRequestV1 Request(VoiceActivityProfileV1 profile,
        params ActivitySourceRequestV1[] sources) => new(profile, ActivityResponsivenessV1.Responsive,
        VoiceActivityNoiseEnvironmentV1.Variable, VoiceActivitySpeechContinuityV1.Natural, null, sources,
        ActivityDegradationPolicyV1.Strict,
        new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static ActivitySourceRequestV1 Source(string key, ActivitySourceKindV1 kind,
        ActivitySourceRoleV1 role) => new(key, kind, role, true);

    private static VoiceActivitySourceCandidateV1 Candidate(string key, ActivitySourceKindV1 kind, int sampleRate,
        VoiceActivityMeasurementKindV1? measurement = null, Hash256? calibration = null,
        ProviderActivityVisibilityV1? visibility = null)
    {
        var opaque = kind == ActivitySourceKindV1.ProviderNative;
        var measurementKind = measurement ?? kind switch
        {
            ActivitySourceKindV1.Manual => VoiceActivityMeasurementKindV1.PostProcessedState,
            ActivitySourceKindV1.SttAdjacent => VoiceActivityMeasurementKindV1.ProviderOpaqueCategory,
            _ => VoiceActivityMeasurementKindV1.EngineScore,
        };
        return new VoiceActivitySourceCandidateV1(key, kind, new VoiceActivitySourceCapabilitiesV1(
            opaque ? VoiceActivityInputOwnershipV1.ProviderOpaque : VoiceActivityInputOwnershipV1.BorrowedSynchronous,
            [opaque ? new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0) :
                new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, sampleRate, 1)],
            new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 1),
            new VoiceActivityMeasurementDescriptorV1(measurementKind, new BoundedAscii("composition"), -1, 1,
                measurementKind == VoiceActivityMeasurementKindV1.CalibratedLikelihood ? calibration : null),
            opaque ? VoiceActivitySourceStateModelV1.ProviderOpaque : VoiceActivitySourceStateModelV1.Stateless,
            opaque ? VoiceActivitySourceConcurrencyV1.ProviderManaged : VoiceActivitySourceConcurrencyV1.Serial,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
            opaque ? VoiceActivitySourceControlV1.Sequenced : VoiceActivitySourceControlV1.Unsupported,
            VoiceActivitySourceControlV1.ReplacementRequired, true, false, opaque ? 4 : 1), true,
            visibility ?? (opaque ? ProviderActivityVisibilityV1.Acknowledged :
                ProviderActivityVisibilityV1.AcceptedLocally));
    }

    private static VoiceActivitySourceOutcomeV1.Observed Observed(bool active, ulong sequence,
        Hash256? calibration = null)
    {
        var descriptor = new VoiceActivityMeasurementDescriptorV1(
            calibration.HasValue ? VoiceActivityMeasurementKindV1.CalibratedLikelihood :
                VoiceActivityMeasurementKindV1.BinaryDecision,
            new BoundedAscii("composition"), -1, 1, calibration);
        return new VoiceActivitySourceOutcomeV1.Observed(
            calibration.HasValue ? new VoiceActivityMeasurementV1.Numeric(active ? 1 : -1) :
                new VoiceActivityMeasurementV1.Binary(active), descriptor,
            new VoiceActivityMediaExtentV1(Graph, (long)sequence * 1_000, ((long)sequence + 1) * 1_000, true),
            sequence, new MonotonicStampV1(Clock, Boot, sequence),
            new MonotonicStampV1(Clock, Boot, sequence));
    }

    private static VoiceActivitySourceOutcomeV1.Observed ProviderObserved(bool active, ulong sequence)
    {
        var descriptor = new VoiceActivityMeasurementDescriptorV1(
            VoiceActivityMeasurementKindV1.ProviderOpaqueCategory, new BoundedAscii("provider-state"), -1, 1, null);
        return new VoiceActivitySourceOutcomeV1.Observed(
            new VoiceActivityMeasurementV1.Category(new BoundedAscii(active ? "active" : "inactive")), descriptor,
            new VoiceActivityMediaExtentV1(Graph, (long)sequence * 1_000, ((long)sequence + 1) * 1_000, true),
            sequence, new MonotonicStampV1(Clock, Boot, sequence),
            new MonotonicStampV1(Clock, Boot, sequence));
    }
}
