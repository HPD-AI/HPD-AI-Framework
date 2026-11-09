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
    var identity = new VoiceActivityCompositionIdentity("example-tenant",
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()));
    var shape = Shape(scenario);
    if (scenario == "finite")
    {
        var support = VoiceActivityCompositionHost.InspectFinite(1, 1, identity, shape.Request, shape.Candidates);
        return Summary(scenario, support, factCount: 0, finite: true);
    }

    var prepared = VoiceActivityCompositionHost.Start(1, 1, identity, GraphDirection.IngressForward,
        shape.Request, shape.Candidates, shape.Generations);
    if (prepared is VoiceActivityCompositionPreparation.Rejected rejected)
        return Summary(scenario, rejected.Support, factCount: 0, finite: false);

    var host = ((VoiceActivityCompositionPreparation.Prepared)prepared).Host;
    if (scenario == "manual")
    {
        Apply(host, "manual", 1, VoiceActivityPromotionEdge.ManualPress, active: true);
        Apply(host, "manual", 2, VoiceActivityPromotionEdge.ManualRelease, active: false);
    }
    else if (scenario == "fusion")
    {
        Apply(host, "local", 1, VoiceActivityPromotionEdge.Observation, active: true, shape.Calibration);
        Apply(host, "provider", 1, VoiceActivityPromotionEdge.Observation, active: true, shape.Calibration);
    }
    else
    {
        var key = host.Snapshot.Sources.Single(static source =>
            source.Role == ActivitySourceRole.Authoritative).SourceKey;
        Apply(host, key, 1, VoiceActivityPromotionEdge.Observation, active: true);
    }
    return Summary(scenario, host.Support, host.Facts.Count, finite: false);
}

static Scenario Shape(string scenario) => scenario switch
{
    "microphone" => Local("microphone", 16_000),
    "webrtc" => Local("webrtc", 48_000),
    "telephony" => Local("telephony", 8_000),
    "finite" => Local("finite-local", 16_000),
    "provider" => Provider(ProviderActivityVisibility.Acknowledged),
    "provider-unknown" => Provider(ProviderActivityVisibility.Unknown),
    "provider-not-observable" => Provider(ProviderActivityVisibility.NotObservable),
    "split" => Split(),
    "fusion" => Fusion(),
    "manual" => Manual(),
    _ => throw new ArgumentException($"Unknown scenario '{scenario}'.", nameof(scenario)),
};

static Scenario Local(string key, int sampleRate) => new(
    Request(VoiceActivityProfile.HpdManaged,
        new ActivitySourceRequest(key, ActivitySourceKind.LocalDetector,
            ActivitySourceRole.Authoritative, true)),
    [Candidate(key, ActivitySourceKind.LocalDetector, sampleRate)],
    new Dictionary<string, ulong> { [key] = 1 }, null);

static Scenario Provider(ProviderActivityVisibility visibility) => new(
    Request(VoiceActivityProfile.ProviderManaged,
        new ActivitySourceRequest("provider", ActivitySourceKind.ProviderNative,
            ActivitySourceRole.Authoritative, true)),
    [Candidate("provider", ActivitySourceKind.ProviderNative, 0, visibility: visibility)],
    new Dictionary<string, ulong> { ["provider"] = 1 }, null);

static Scenario Split() => new(
    Request(VoiceActivityProfile.HpdManaged,
        new ActivitySourceRequest("local", ActivitySourceKind.LocalDetector,
            ActivitySourceRole.Authoritative, true),
        new ActivitySourceRequest("stt", ActivitySourceKind.SttAdjacent,
            ActivitySourceRole.Fallback, true)),
    [Candidate("local", ActivitySourceKind.LocalDetector, 16_000),
     Candidate("stt", ActivitySourceKind.SttAdjacent, 16_000)],
    new Dictionary<string, ulong> { ["local"] = 1 }, null);

static Scenario Fusion()
{
    var calibration = Hash256.Compute("composition-example"u8);
    return new Scenario(
        Request(VoiceActivityProfile.Fused,
            new ActivitySourceRequest("local", ActivitySourceKind.LocalDetector,
                ActivitySourceRole.Authoritative, true),
            new ActivitySourceRequest("provider", ActivitySourceKind.ProviderNative,
                ActivitySourceRole.Corroborating, true)),
        [Candidate("local", ActivitySourceKind.LocalDetector, 16_000,
             VoiceActivityMeasurementKind.CalibratedLikelihood, calibration),
         Candidate("provider", ActivitySourceKind.ProviderNative, 0,
             VoiceActivityMeasurementKind.CalibratedLikelihood, calibration)],
        new Dictionary<string, ulong> { ["local"] = 1, ["provider"] = 1 }, calibration);
}

static Scenario Manual() => new(
    Request(VoiceActivityProfile.Manual,
        new ActivitySourceRequest("manual", ActivitySourceKind.Manual,
            ActivitySourceRole.Authoritative, true)),
    [Candidate("manual", ActivitySourceKind.Manual, 16_000)],
    new Dictionary<string, ulong> { ["manual"] = 1 }, null);

static VoiceActivityRequest Request(VoiceActivityProfile profile,
    params ActivitySourceRequest[] sources) => new(profile, ActivityResponsiveness.Responsive,
    VoiceActivityNoiseEnvironment.Variable, VoiceActivitySpeechContinuity.Natural, null, sources,
    ActivityDegradationPolicy.Strict,
    new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

static VoiceActivitySourceCandidate Candidate(string key, ActivitySourceKind kind, int sampleRate,
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

static void Apply(VoiceActivityCompositionHost host, string key, ulong sequence,
    VoiceActivityPromotionEdge edge, bool active, Hash256? calibration = null)
{
    var descriptor = new VoiceActivityMeasurementDescriptor(
        calibration.HasValue ? VoiceActivityMeasurementKind.CalibratedLikelihood :
            VoiceActivityMeasurementKind.BinaryDecision,
        new BoundedAscii("composition"), -1, 1, calibration);
    var stamp = new MonotonicStampV1(ExampleAuthority.Clock, ExampleAuthority.Boot, sequence);
    var outcome = new VoiceActivitySourceOutcome.Observed(
        calibration.HasValue ? new VoiceActivityMeasurement.Numeric(active ? 1 : -1) :
            new VoiceActivityMeasurement.Binary(active), descriptor,
        new VoiceActivityMediaExtent(ExampleAuthority.Graph, (long)sequence * 1_000,
            ((long)sequence + 1) * 1_000, true), sequence, stamp, stamp);
    if (host.Apply(key, sequence, edge, outcome) is VoiceActivityPromotionResult.Rejected rejected)
        throw new InvalidOperationException($"Scenario input was rejected: {rejected.SafeCode}");
}

static string Summary(string scenario, VoiceActivityCompositionSupportBundle support, int factCount, bool finite)
{
    var sourceSummary = support.Effective is null
        ? "none"
        : string.Join(',', support.Effective.Sources.Select(static source =>
            $"{source.SourceKey}:{source.Kind}:{source.ProviderVisibility}"));
    return $"scenario={scenario} requested={support.Request.Profile} effective={support.Effective?.Health.ToString() ?? "Rejected"} " +
        $"sources={sourceSummary} safe-code={support.SafeCode ?? "none"} facts={factCount} finite={finite.ToString().ToLowerInvariant()}";
}

file sealed record Scenario(VoiceActivityRequest Request,
    IReadOnlyList<VoiceActivitySourceCandidate> Candidates,
    IReadOnlyDictionary<string, ulong> Generations,
    Hash256? Calibration);

file static class ExampleAuthority
{
    internal static readonly ClockDomainId Clock = ClockDomainId.Create();
    internal static readonly BootId Boot = BootId.Create();
    internal static readonly GraphGenerationId Graph = GraphGenerationId.Create();
}
