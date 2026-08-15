using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;

var limits = new VoiceActivityOperationalLimitsV1(
    maximumSources: 2,
    maximumObservationHistory: 32,
    maximumCorrectionHistory: 4,
    maximumWindow: TimeSpan.FromSeconds(1),
    maximumProcessingLatency: TimeSpan.FromMilliseconds(250));
var request = new VoiceActivityRequestV1(
    VoiceActivityProfileV1.Fused,
    ActivityResponsivenessV1.Balanced,
    VoiceActivityNoiseEnvironmentV1.Variable,
    VoiceActivitySpeechContinuityV1.Natural,
    TimeSpan.FromMilliseconds(200),
    [
        new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector,
            ActivitySourceRoleV1.Authoritative, required: true),
        new ActivitySourceRequestV1("provider", ActivitySourceKindV1.ProviderNative,
            ActivitySourceRoleV1.Corroborating, required: false),
    ],
    ActivityDegradationPolicyV1.AllowOptionalSources,
    limits);

var encoded = JsonSerializer.SerializeToUtf8Bytes(request, VoiceActivityJsonContextV1.Default.VoiceActivityRequestV1);
var decoded = JsonSerializer.Deserialize(encoded, VoiceActivityJsonContextV1.Default.VoiceActivityRequestV1)
    ?? throw new InvalidOperationException("The request did not roundtrip.");

if (decoded.Profile != request.Profile || decoded.Sources.Count != 2 || decoded.Sources[0].SourceKey != "local")
    throw new InvalidOperationException("The generated metadata changed immutable voice-activity intent.");
if (encoded.Length == 0)
    throw new InvalidOperationException("The generated payload is empty.");

var provider = new SmokeProvider();
var product = VoiceActivitySourceProviderBindingV1.Create(provider,
    new ProviderClientConfig { ProviderKey = provider.ProviderKey, ModelName = "native-vad" },
    new ProviderComponentLifetimeContext(AudioSessionId: "native-audio",
        Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession));
if (product is not VoiceActivitySourceProductV1.BorrowedSynchronous)
    throw new InvalidOperationException("The typed provider product did not survive native binding.");

Console.WriteLine("voice-activity-aot=pass");

sealed class SmokeProvider : IVoiceActivitySourceProviderV1
{
    public string ProviderKey => "native-smoke";
    public string DisplayName => "Native smoke";
    public VoiceActivitySourceProductV1 CreateVoiceActivitySource(ProviderClientConfig configuration,
        ProviderComponentLifetimeContext context, IServiceProvider? services = null) =>
        new VoiceActivitySourceProductV1.BorrowedSynchronous(new SmokeSource());
    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.VoiceActivityDetection] = new()
            {
                Family = ProviderClientFamily.VoiceActivityDetection,
                Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession,
            },
        },
    };
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) =>
        ProviderValidationResult.Success();
    public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
}

sealed class SmokeSource : IBorrowedSynchronousVoiceActivitySourceV1
{
    public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = new(
        VoiceActivityInputOwnershipV1.BorrowedSynchronous,
        [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.BinaryDecision,
            new HPD.Agent.Authority.BoundedAscii("decision"), 0, 1, null),
        VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
        true, false, 1);

    public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window) =>
        new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Gap);
}
