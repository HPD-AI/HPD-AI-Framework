using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityPromotionV1Tests
{
    private static readonly RuntimeGenerationId Runtime = RuntimeGenerationId.Create();
    private static readonly LiveSessionId LiveSession = LiveSessionId.Create();
    private static readonly SessionAuthorityStampV1 Session = new(Runtime, LiveSession);
    private static readonly GraphGenerationId Graph = GraphGenerationId.Create();
    private static readonly ClockDomainId Clock = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();

    [Fact]
    public void Balanced_hysteresis_promotes_bounded_exact_facts()
    {
        var promoter = Promoter(Plan(ActivityResponsivenessV1.Balanced));

        Assert.Equal(VoiceActivityPromotionStateV1.CandidateOpen,
            Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(1, true, 0, 10, false))).State);
        var opened = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(2, true, 10, 20))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKindV1.Opened, opened.Kind);
        Assert.Equal((0, 20, false), (opened.Extent!.Value.StartInclusive, opened.Extent.Value.EndExclusive, opened.Extent.Value.Exact));

        Assert.Equal(VoiceActivityPromotionStateV1.CandidateClose,
            Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(3, false, 20, 30))).State);
        var closed = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(4, false, 30, 40))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKindV1.Closed, closed.Kind);
        Assert.Equal(Session, closed.Session);
        Assert.Equal(GraphDirectionV1.IngressForward, closed.Direction);
    }

    [Fact]
    public void Candidate_reversal_is_a_false_start_not_a_close()
    {
        var promoter = Promoter(Plan(ActivityResponsivenessV1.Conservative));
        promoter.Apply(Input(1, true, 0, 10));
        var fact = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(2, false, 10, 20))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKindV1.FalseStart, fact.Kind);
        Assert.DoesNotContain(promoter.Facts, static row => row.Kind == VoiceActivityPromotionFactKindV1.Closed);
    }

    [Fact]
    public void Replay_duplicate_contradiction_and_authority_fences_are_closed()
    {
        var promoter = Promoter(Plan(ActivityResponsivenessV1.Responsive));
        var first = Input(1, true, 0, 10);
        Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(first));
        Assert.IsType<VoiceActivityPromotionResultV1.Duplicate>(promoter.Apply(first));
        Assert.Equal("source-sequence-contradiction", Assert.IsType<VoiceActivityPromotionResultV1.Rejected>(
            promoter.Apply(Input(1, false, 0, 10))).SafeCode);
        Assert.IsType<VoiceActivityPromotionResultV1.Stale>(promoter.Apply(Input(2, true, 10, 20,
            session: new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSession))));
        Assert.IsType<VoiceActivityPromotionResultV1.Stale>(promoter.Apply(Input(2, true, 10, 20,
            direction: GraphDirectionV1.EgressForward)));
    }

    [Fact]
    public void Fault_gap_and_discontinuity_never_synthesize_a_stop()
    {
        var promoter = Promoter(Plan(ActivityResponsivenessV1.Responsive));
        promoter.Apply(Input(1, true, 0, 10));
        var fault = new VoiceActivitySourceOutcomeV1.Fault(VoiceActivitySourceFaultClassV1.ProviderFailure,
            VoiceActivityStateValidityV1.ResetRequired, VoiceActivityRetryabilityV1.SameGeneration);
        Assert.Equal(VoiceActivityPromotionFactKindV1.Faulted,
            Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(2, fault))).Fact!.Kind);
        Assert.DoesNotContain(promoter.Facts, static row => row.Kind == VoiceActivityPromotionFactKindV1.Closed);
        Assert.Equal(VoiceActivityPromotionFactKindV1.Discontinuous,
            Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Discontinuity(3))).Fact!.Kind);
        Assert.DoesNotContain(promoter.Facts, static row => row.Kind == VoiceActivityPromotionFactKindV1.Closed);
    }

    [Fact]
    public void Fused_sources_have_one_union_promoter_and_stable_contributor_order()
    {
        var plan = Plan(ActivityResponsivenessV1.Responsive, fused: true);
        var promoter = Promoter(plan, new Dictionary<string, ulong> { ["local"] = 1, ["provider"] = 1 });
        promoter.Apply(Input(1, true, 0, 10, source: "provider"));
        var fact = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(
            promoter.Apply(Input(1, true, 0, 10, source: "local"))).Fact!;
        Assert.Equal(["local", "provider"], fact.Contributors);
    }

    [Fact]
    public void Correction_is_latest_only_and_bounded()
    {
        var promoter = Promoter(Plan(ActivityResponsivenessV1.Responsive));
        var opened = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(1, true, 0, 10))).Fact!;
        var corrected = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(
            Input(2, false, 0, 10, edge: VoiceActivityPromotionEdgeV1.Correction,
                corrects: opened.PromotionSequence))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKindV1.Corrected, corrected.Kind);
        Assert.Equal(opened.PromotionSequence, corrected.CorrectsPromotionSequence);
        Assert.Equal("correction-target-stale", Assert.IsType<VoiceActivityPromotionResultV1.Rejected>(
            promoter.Apply(Input(3, true, 10, 20, edge: VoiceActivityPromotionEdgeV1.Correction,
                corrects: opened.PromotionSequence))).SafeCode);
    }

    [Fact]
    public void Critical_evidence_buffer_is_ordered_bounded_and_idempotent()
    {
        var promoter = Promoter(Plan(ActivityResponsivenessV1.Responsive));
        var one = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(1, true, 0, 10))).Fact!;
        var two = Assert.IsType<VoiceActivityPromotionResultV1.Applied>(promoter.Apply(Input(2, false, 10, 20))).Fact!;
        var buffer = new VoiceActivityCriticalEvidenceBufferV1(1, 1, Session,
            GraphDirectionV1.IngressForward, 2);
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResultV1.Appended, buffer.Append(one));
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResultV1.Duplicate, buffer.Append(one));
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResultV1.Appended, buffer.Append(two));
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResultV1.Stale, buffer.Append(one));
        Assert.Equal([one, two], buffer.Snapshot);
    }

    [Fact]
    public void Numeric_fusion_requires_matching_calibrated_semantics()
    {
        var request = Request(ActivityResponsivenessV1.Responsive, true);
        var rejected = Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request,
            [
                Candidate("local", ActivitySourceKindV1.LocalDetector, "engine-a"),
                Candidate("provider", ActivitySourceKindV1.ProviderNative, "engine-b"),
            ]));
        Assert.Equal("fusion-measurement-incompatible", rejected.SafeCode);
    }

    private static VoiceActivityPromoterV1 Promoter(VoiceActivityEffectivePlanV1 plan,
        IReadOnlyDictionary<string, ulong>? generations = null) => new(plan, Session,
        GraphDirectionV1.IngressForward, generations ?? new Dictionary<string, ulong> { ["local"] = 1 });

    private static VoiceActivityEffectivePlanV1 Plan(ActivityResponsivenessV1 responsiveness, bool fused = false) =>
        Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(VoiceActivityPlanCompilerV1.Compile(1, 1,
            Request(responsiveness, fused), fused
                ? [Candidate("local", ActivitySourceKindV1.LocalDetector, "shared"), Candidate("provider", ActivitySourceKindV1.ProviderNative, "shared")]
                : [Candidate("local", ActivitySourceKindV1.LocalDetector, "shared")])).Plan;

    private static VoiceActivityRequestV1 Request(ActivityResponsivenessV1 responsiveness, bool fused) => new(
        fused ? VoiceActivityProfileV1.Fused : VoiceActivityProfileV1.HpdManaged, responsiveness,
        VoiceActivityNoiseEnvironmentV1.Variable, VoiceActivitySpeechContinuityV1.Natural, null,
        fused
            ? [new("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true),
               new("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Corroborating, true)]
            : [new("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true)],
        ActivityDegradationPolicyV1.Strict,
        new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static VoiceActivitySourceCandidateV1 Candidate(string key, ActivitySourceKindV1 kind, string semantic) => new(
        key, kind, new VoiceActivitySourceCapabilitiesV1(
            kind == ActivitySourceKindV1.ProviderNative ? VoiceActivityInputOwnershipV1.ProviderOpaque : VoiceActivityInputOwnershipV1.BorrowedSynchronous,
            [kind == ActivitySourceKindV1.ProviderNative ? new(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0) : new(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
            new(VoiceActivityMeasurementKindV1.CalibratedLikelihood, new BoundedAscii(semantic), -1, 1,
                Hash256.Compute(System.Text.Encoding.ASCII.GetBytes(semantic))),
            kind == ActivitySourceKindV1.ProviderNative ? VoiceActivitySourceStateModelV1.ProviderOpaque : VoiceActivitySourceStateModelV1.Stateless,
            kind == ActivitySourceKindV1.ProviderNative ? VoiceActivitySourceConcurrencyV1.ProviderManaged : VoiceActivitySourceConcurrencyV1.Serial,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
            kind == ActivitySourceKindV1.ProviderNative ? VoiceActivitySourceControlV1.Sequenced : VoiceActivitySourceControlV1.Unsupported,
            VoiceActivitySourceControlV1.ReplacementRequired, true, false, kind == ActivitySourceKindV1.ProviderNative ? 4 : 1),
        true, kind == ActivitySourceKindV1.ProviderNative ? ProviderActivityVisibilityV1.Acknowledged : ProviderActivityVisibilityV1.AcceptedLocally);

    private static VoiceActivityPromotionInputV1 Input(ulong sequence, bool active, long start, long end,
        bool exact = true, string source = "local", VoiceActivityPromotionEdgeV1 edge = VoiceActivityPromotionEdgeV1.Observation,
        ulong corrects = 0, SessionAuthorityStampV1? session = null, GraphDirectionV1 direction = GraphDirectionV1.IngressForward) =>
        Input(sequence, new VoiceActivitySourceOutcomeV1.Observed(new VoiceActivityMeasurementV1.Numeric(active ? 1 : -1),
            new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.CalibratedLikelihood,
                new BoundedAscii("shared"), -1, 1,
                Hash256.Compute(System.Text.Encoding.ASCII.GetBytes("shared"))),
            new VoiceActivityMediaExtentV1(Graph, start, end, exact),
            sequence, new MonotonicStampV1(Clock, Boot, sequence), new MonotonicStampV1(Clock, Boot, sequence)),
            source, edge, corrects, session, direction);

    private static VoiceActivityPromotionInputV1 Input(ulong sequence, VoiceActivitySourceOutcomeV1 outcome,
        string source = "local", VoiceActivityPromotionEdgeV1 edge = VoiceActivityPromotionEdgeV1.Observation,
        ulong corrects = 0, SessionAuthorityStampV1? session = null, GraphDirectionV1 direction = GraphDirectionV1.IngressForward) =>
        new(1, 1, session ?? Session, direction, source, 1, sequence, edge, outcome, corrects);

    private static VoiceActivityPromotionInputV1 Discontinuity(ulong sequence) =>
        new(1, 1, Session, GraphDirectionV1.IngressForward, "local", 1, sequence,
            VoiceActivityPromotionEdgeV1.Discontinuity, null);
}
