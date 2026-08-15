using System.Text.Json;
using HPD.Agent.Audio.VoiceActivity;

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

Console.WriteLine("voice-activity-aot=pass");
