using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityPromotionTests
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
        var promoter = Promoter(Plan(ActivityResponsiveness.Balanced));

        Assert.Equal(VoiceActivityPromotionState.CandidateOpen,
            Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(1, true, 0, 10, false))).State);
        var opened = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(2, true, 10, 20))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKind.Opened, opened.Kind);
        Assert.Equal((0, 20, false), (opened.Extent!.Value.StartInclusive, opened.Extent.Value.EndExclusive, opened.Extent.Value.Exact));

        Assert.Equal(VoiceActivityPromotionState.CandidateClose,
            Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(3, false, 20, 30))).State);
        var closed = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(4, false, 30, 40))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKind.Closed, closed.Kind);
        Assert.Equal(Session, closed.Session);
        Assert.Equal(GraphDirection.IngressForward, closed.Direction);
    }

    [Fact]
    public void Candidate_reversal_is_a_false_start_not_a_close()
    {
        var promoter = Promoter(Plan(ActivityResponsiveness.Conservative));
        promoter.Apply(Input(1, true, 0, 10));
        var fact = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(2, false, 10, 20))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKind.FalseStart, fact.Kind);
        Assert.DoesNotContain(promoter.Facts, static row => row.Kind == VoiceActivityPromotionFactKind.Closed);
    }

    [Fact]
    public void Replay_duplicate_contradiction_and_authority_fences_are_closed()
    {
        var promoter = Promoter(Plan(ActivityResponsiveness.Responsive));
        var first = Input(1, true, 0, 10);
        Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(first));
        Assert.IsType<VoiceActivityPromotionResult.Duplicate>(promoter.Apply(first));
        Assert.Equal("source-sequence-contradiction", Assert.IsType<VoiceActivityPromotionResult.Rejected>(
            promoter.Apply(Input(1, false, 0, 10))).SafeCode);
        Assert.IsType<VoiceActivityPromotionResult.Stale>(promoter.Apply(Input(2, true, 10, 20,
            session: new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSession))));
        Assert.IsType<VoiceActivityPromotionResult.Stale>(promoter.Apply(Input(2, true, 10, 20,
            direction: GraphDirection.EgressForward)));
    }

    [Fact]
    public void Fault_gap_and_discontinuity_never_synthesize_a_stop()
    {
        var promoter = Promoter(Plan(ActivityResponsiveness.Responsive));
        promoter.Apply(Input(1, true, 0, 10));
        var fault = new VoiceActivitySourceOutcome.Fault(VoiceActivitySourceFaultClass.ProviderFailure,
            VoiceActivityStateValidity.ResetRequired, VoiceActivityRetryability.SameGeneration);
        Assert.Equal(VoiceActivityPromotionFactKind.Faulted,
            Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(2, fault))).Fact!.Kind);
        Assert.DoesNotContain(promoter.Facts, static row => row.Kind == VoiceActivityPromotionFactKind.Closed);
        Assert.Equal(VoiceActivityPromotionFactKind.Discontinuous,
            Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Discontinuity(3))).Fact!.Kind);
        Assert.DoesNotContain(promoter.Facts, static row => row.Kind == VoiceActivityPromotionFactKind.Closed);
    }

    [Fact]
    public void Fused_sources_have_one_union_promoter_and_stable_contributor_order()
    {
        var plan = Plan(ActivityResponsiveness.Responsive, fused: true);
        var promoter = Promoter(plan, new Dictionary<string, ulong> { ["local"] = 1, ["provider"] = 1 });
        promoter.Apply(Input(1, true, 0, 10, source: "provider"));
        var fact = Assert.IsType<VoiceActivityPromotionResult.Applied>(
            promoter.Apply(Input(1, true, 0, 10, source: "local"))).Fact!;
        Assert.Equal(["local", "provider"], fact.Contributors);
    }

    [Fact]
    public void Correction_is_latest_only_and_bounded()
    {
        var promoter = Promoter(Plan(ActivityResponsiveness.Responsive));
        var opened = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(1, true, 0, 10))).Fact!;
        var corrected = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(
            Input(2, false, 0, 10, edge: VoiceActivityPromotionEdge.Correction,
                corrects: opened.PromotionSequence))).Fact!;
        Assert.Equal(VoiceActivityPromotionFactKind.Corrected, corrected.Kind);
        Assert.Equal(opened.PromotionSequence, corrected.CorrectsPromotionSequence);
        Assert.Equal("correction-target-stale", Assert.IsType<VoiceActivityPromotionResult.Rejected>(
            promoter.Apply(Input(3, true, 10, 20, edge: VoiceActivityPromotionEdge.Correction,
                corrects: opened.PromotionSequence))).SafeCode);
    }

    [Fact]
    public void Critical_evidence_buffer_is_ordered_bounded_and_idempotent()
    {
        var promoter = Promoter(Plan(ActivityResponsiveness.Responsive));
        var one = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(1, true, 0, 10))).Fact!;
        var two = Assert.IsType<VoiceActivityPromotionResult.Applied>(promoter.Apply(Input(2, false, 10, 20))).Fact!;
        var buffer = new VoiceActivityCriticalEvidenceBuffer(1, 1, Session,
            GraphDirection.IngressForward, 2);
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResult.Appended, buffer.Append(one));
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResult.Duplicate, buffer.Append(one));
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResult.Appended, buffer.Append(two));
        Assert.Equal(VoiceActivityCriticalEvidenceAppendResult.Stale, buffer.Append(one));
        Assert.Equal([one, two], buffer.Snapshot);
    }

    [Fact]
    public void Numeric_fusion_requires_matching_calibrated_semantics()
    {
        var request = Request(ActivityResponsiveness.Responsive, true);
        var rejected = Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, request,
            [
                Candidate("local", ActivitySourceKind.LocalDetector, "engine-a"),
                Candidate("provider", ActivitySourceKind.ProviderNative, "engine-b"),
            ]));
        Assert.Equal("fusion-measurement-incompatible", rejected.SafeCode);
    }

    private static VoiceActivityPromoter Promoter(VoiceActivityEffectivePlan plan,
        IReadOnlyDictionary<string, ulong>? generations = null) => new(plan, Session,
        GraphDirection.IngressForward, generations ?? new Dictionary<string, ulong> { ["local"] = 1 });

    private static VoiceActivityEffectivePlan Plan(ActivityResponsiveness responsiveness, bool fused = false) =>
        Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(VoiceActivityPlanCompiler.Compile(1, 1,
            Request(responsiveness, fused), fused
                ? [Candidate("local", ActivitySourceKind.LocalDetector, "shared"), Candidate("provider", ActivitySourceKind.ProviderNative, "shared")]
                : [Candidate("local", ActivitySourceKind.LocalDetector, "shared")])).Plan;

    private static VoiceActivityRequest Request(ActivityResponsiveness responsiveness, bool fused) => new(
        fused ? VoiceActivityProfile.Fused : VoiceActivityProfile.HpdManaged, responsiveness,
        VoiceActivityNoiseEnvironment.Variable, VoiceActivitySpeechContinuity.Natural, null,
        fused
            ? [new("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true),
               new("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Corroborating, true)]
            : [new("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true)],
        ActivityDegradationPolicy.Strict,
        new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static VoiceActivitySourceCandidate Candidate(string key, ActivitySourceKind kind, string semantic) => new(
        key, kind, new VoiceActivitySourceCapabilities(
            kind == ActivitySourceKind.ProviderNative ? VoiceActivityInputOwnership.ProviderOpaque : VoiceActivityInputOwnership.BorrowedSynchronous,
            [kind == ActivitySourceKind.ProviderNative ? new(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0) : new(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 1),
            new(VoiceActivityMeasurementKind.CalibratedLikelihood, new BoundedAscii(semantic), -1, 1,
                Hash256.Compute(System.Text.Encoding.ASCII.GetBytes(semantic))),
            kind == ActivitySourceKind.ProviderNative ? VoiceActivitySourceStateModel.ProviderOpaque : VoiceActivitySourceStateModel.Stateless,
            kind == ActivitySourceKind.ProviderNative ? VoiceActivitySourceConcurrency.ProviderManaged : VoiceActivitySourceConcurrency.Serial,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
            kind == ActivitySourceKind.ProviderNative ? VoiceActivitySourceControl.Sequenced : VoiceActivitySourceControl.Unsupported,
            VoiceActivitySourceControl.ReplacementRequired, true, false, kind == ActivitySourceKind.ProviderNative ? 4 : 1),
        true, kind == ActivitySourceKind.ProviderNative ? ProviderActivityVisibility.Acknowledged : ProviderActivityVisibility.AcceptedLocally);

    private static VoiceActivityPromotionInput Input(ulong sequence, bool active, long start, long end,
        bool exact = true, string source = "local", VoiceActivityPromotionEdge edge = VoiceActivityPromotionEdge.Observation,
        ulong corrects = 0, SessionAuthorityStampV1? session = null, GraphDirection direction = GraphDirection.IngressForward) =>
        Input(sequence, new VoiceActivitySourceOutcome.Observed(new VoiceActivityMeasurement.Numeric(active ? 1 : -1),
            new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.CalibratedLikelihood,
                new BoundedAscii("shared"), -1, 1,
                Hash256.Compute(System.Text.Encoding.ASCII.GetBytes("shared"))),
            new VoiceActivityMediaExtent(Graph, start, end, exact),
            sequence, new MonotonicStampV1(Clock, Boot, sequence), new MonotonicStampV1(Clock, Boot, sequence)),
            source, edge, corrects, session, direction);

    private static VoiceActivityPromotionInput Input(ulong sequence, VoiceActivitySourceOutcome outcome,
        string source = "local", VoiceActivityPromotionEdge edge = VoiceActivityPromotionEdge.Observation,
        ulong corrects = 0, SessionAuthorityStampV1? session = null, GraphDirection direction = GraphDirection.IngressForward) =>
        new(1, 1, session ?? Session, direction, source, 1, sequence, edge, outcome, corrects);

    private static VoiceActivityPromotionInput Discontinuity(ulong sequence) =>
        new(1, 1, Session, GraphDirection.IngressForward, "local", 1, sequence,
            VoiceActivityPromotionEdge.Discontinuity, null);
}
