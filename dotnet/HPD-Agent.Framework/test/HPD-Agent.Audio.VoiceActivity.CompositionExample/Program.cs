using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

var requested = args.Length == 0 ? "all" : args[0];
var scenarios = requested == "all"
    ? new[] { "microphone", "webrtc", "telephony", "provider", "split", "fusion", "manual",
        "finite", "provider-unknown", "provider-not-observable" }
    : new[] { requested };

foreach (var scenario in scenarios)
    Console.WriteLine(Run(scenario));

static string Run(string scenario)
{
    var identity = new VoiceActivityCompositionIdentityV1("example-tenant",
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()));
    var shape = Shape(scenario);
    if (scenario == "finite")
    {
        var support = VoiceActivityCompositionHostV1.InspectFinite(1, 1, identity, shape.Request, shape.Candidates);
        return Summary(scenario, support, factCount: 0, finite: true);
    }

    var prepared = VoiceActivityCompositionHostV1.Start(1, 1, identity, GraphDirectionV1.IngressForward,
        shape.Request, shape.Candidates, shape.Generations);
    if (prepared is VoiceActivityCompositionPreparationV1.Rejected rejected)
        return Summary(scenario, rejected.Support, factCount: 0, finite: false);

    var host = ((VoiceActivityCompositionPreparationV1.Prepared)prepared).Host;
    if (scenario == "manual")
    {
        Apply(host, "manual", 1, VoiceActivityPromotionEdgeV1.ManualPress, active: true);
        Apply(host, "manual", 2, VoiceActivityPromotionEdgeV1.ManualRelease, active: false);
    }
    else if (scenario == "fusion")
    {
        Apply(host, "local", 1, VoiceActivityPromotionEdgeV1.Observation, active: true, shape.Calibration);
        Apply(host, "provider", 1, VoiceActivityPromotionEdgeV1.Observation, active: true, shape.Calibration);
    }
    else
    {
        var key = host.Snapshot.Sources.Single(static source =>
            source.Role == ActivitySourceRoleV1.Authoritative).SourceKey;
        Apply(host, key, 1, VoiceActivityPromotionEdgeV1.Observation, active: true);
    }
    return Summary(scenario, host.Support, host.Facts.Count, finite: false);
}

static Scenario Shape(string scenario) => scenario switch
{
    "microphone" => Local("microphone", 16_000),
    "webrtc" => Local("webrtc", 48_000),
    "telephony" => Local("telephony", 8_000),
    "finite" => Local("finite-local", 16_000),
    "provider" => Provider(ProviderActivityVisibilityV1.Acknowledged),
    "provider-unknown" => Provider(ProviderActivityVisibilityV1.Unknown),
    "provider-not-observable" => Provider(ProviderActivityVisibilityV1.NotObservable),
    "split" => Split(),
    "fusion" => Fusion(),
    "manual" => Manual(),
    _ => throw new ArgumentException($"Unknown scenario '{scenario}'.", nameof(scenario)),
};

static Scenario Local(string key, int sampleRate) => new(
    Request(VoiceActivityProfileV1.HpdManaged,
        new ActivitySourceRequestV1(key, ActivitySourceKindV1.LocalDetector,
            ActivitySourceRoleV1.Authoritative, true)),
    [Candidate(key, ActivitySourceKindV1.LocalDetector, sampleRate)],
    new Dictionary<string, ulong> { [key] = 1 }, null);

static Scenario Provider(ProviderActivityVisibilityV1 visibility) => new(
    Request(VoiceActivityProfileV1.ProviderManaged,
        new ActivitySourceRequestV1("provider", ActivitySourceKindV1.ProviderNative,
            ActivitySourceRoleV1.Authoritative, true)),
    [Candidate("provider", ActivitySourceKindV1.ProviderNative, 0, visibility: visibility)],
    new Dictionary<string, ulong> { ["provider"] = 1 }, null);

static Scenario Split() => new(
    Request(VoiceActivityProfileV1.HpdManaged,
        new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector,
            ActivitySourceRoleV1.Authoritative, true),
        new ActivitySourceRequestV1("stt", ActivitySourceKindV1.SttAdjacent,
            ActivitySourceRoleV1.Fallback, true)),
    [Candidate("local", ActivitySourceKindV1.LocalDetector, 16_000),
     Candidate("stt", ActivitySourceKindV1.SttAdjacent, 16_000)],
    new Dictionary<string, ulong> { ["local"] = 1 }, null);

static Scenario Fusion()
{
    var calibration = Hash256.Compute("composition-example"u8);
    return new Scenario(
        Request(VoiceActivityProfileV1.Fused,
            new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector,
                ActivitySourceRoleV1.Authoritative, true),
            new ActivitySourceRequestV1("provider", ActivitySourceKindV1.ProviderNative,
                ActivitySourceRoleV1.Corroborating, true)),
        [Candidate("local", ActivitySourceKindV1.LocalDetector, 16_000,
             VoiceActivityMeasurementKindV1.CalibratedLikelihood, calibration),
         Candidate("provider", ActivitySourceKindV1.ProviderNative, 0,
             VoiceActivityMeasurementKindV1.CalibratedLikelihood, calibration)],
        new Dictionary<string, ulong> { ["local"] = 1, ["provider"] = 1 }, calibration);
}

static Scenario Manual() => new(
    Request(VoiceActivityProfileV1.Manual,
        new ActivitySourceRequestV1("manual", ActivitySourceKindV1.Manual,
            ActivitySourceRoleV1.Authoritative, true)),
    [Candidate("manual", ActivitySourceKindV1.Manual, 16_000)],
    new Dictionary<string, ulong> { ["manual"] = 1 }, null);

static VoiceActivityRequestV1 Request(VoiceActivityProfileV1 profile,
    params ActivitySourceRequestV1[] sources) => new(profile, ActivityResponsivenessV1.Responsive,
    VoiceActivityNoiseEnvironmentV1.Variable, VoiceActivitySpeechContinuityV1.Natural, null, sources,
    ActivityDegradationPolicyV1.Strict,
    new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

static VoiceActivitySourceCandidateV1 Candidate(string key, ActivitySourceKindV1 kind, int sampleRate,
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

static void Apply(VoiceActivityCompositionHostV1 host, string key, ulong sequence,
    VoiceActivityPromotionEdgeV1 edge, bool active, Hash256? calibration = null)
{
    var descriptor = new VoiceActivityMeasurementDescriptorV1(
        calibration.HasValue ? VoiceActivityMeasurementKindV1.CalibratedLikelihood :
            VoiceActivityMeasurementKindV1.BinaryDecision,
        new BoundedAscii("composition"), -1, 1, calibration);
    var stamp = new MonotonicStampV1(ExampleAuthority.Clock, ExampleAuthority.Boot, sequence);
    var outcome = new VoiceActivitySourceOutcomeV1.Observed(
        calibration.HasValue ? new VoiceActivityMeasurementV1.Numeric(active ? 1 : -1) :
            new VoiceActivityMeasurementV1.Binary(active), descriptor,
        new VoiceActivityMediaExtentV1(ExampleAuthority.Graph, (long)sequence * 1_000,
            ((long)sequence + 1) * 1_000, true), sequence, stamp, stamp);
    if (host.Apply(key, sequence, edge, outcome) is VoiceActivityPromotionResultV1.Rejected rejected)
        throw new InvalidOperationException($"Scenario input was rejected: {rejected.SafeCode}");
}

static string Summary(string scenario, VoiceActivityCompositionSupportBundleV1 support, int factCount, bool finite)
{
    var sourceSummary = support.Effective is null
        ? "none"
        : string.Join(',', support.Effective.Sources.Select(static source =>
            $"{source.SourceKey}:{source.Kind}:{source.ProviderVisibility}"));
    return $"scenario={scenario} requested={support.Request.Profile} effective={support.Effective?.Health.ToString() ?? "Rejected"} " +
        $"sources={sourceSummary} safe-code={support.SafeCode ?? "none"} facts={factCount} finite={finite.ToString().ToLowerInvariant()}";
}

file sealed record Scenario(VoiceActivityRequestV1 Request,
    IReadOnlyList<VoiceActivitySourceCandidateV1> Candidates,
    IReadOnlyDictionary<string, ulong> Generations,
    Hash256? Calibration);

file static class ExampleAuthority
{
    internal static readonly ClockDomainId Clock = ClockDomainId.Create();
    internal static readonly BootId Boot = BootId.Create();
    internal static readonly GraphGenerationId Graph = GraphGenerationId.Create();
}
