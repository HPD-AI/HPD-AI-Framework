using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Audio.LiveKit;

[AudioSessionBinding(
    Component = LiveKitAudioTransport.ComponentInstance,
    Schema = LiveKitAudioTransport.SessionBindingSchema,
    Version = LiveKitAudioTransport.SessionBindingVersion)]
public sealed record LiveKitAudioSessionBinding
{
    public required string RoomName { get; init; }
    public required string ParticipantIdentity { get; init; }
    public string? RemoteParticipantIdentity { get; init; }
    public string? ParticipantName { get; init; }
    public string? ParticipantMetadata { get; init; }
}

public static class LiveKitAudioTransport
{
    public const string ComponentInstance = "livekit";
    public const string SessionBindingSchema = "hpd.provider.livekit.audiotransport.sessionbinding";
    public const uint SessionBindingVersion = 1;

    public static AudioSessionStartBindings Bindings(LiveKitAudioSessionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new AudioSessionStartBindings
        {
            Bindings =
            [
                new AudioSessionStartBinding
                {
                    ComponentInstance = ComponentInstance,
                    Schema = SessionBindingSchema,
                    Version = SessionBindingVersion,
                    Value = JsonSerializer.SerializeToElement(
                        binding, LiveKitManagedSessionJsonContext.Default.LiveKitAudioSessionBinding)
                }
            ]
        };
    }

    internal static LiveKitAudioSessionBinding Decode(AudioSessionStartBindings bindings)
    {
        var value = bindings.Bindings.SingleOrDefault(item =>
            string.Equals(item.ComponentInstance, ComponentInstance, StringComparison.Ordinal) &&
            string.Equals(item.Schema, SessionBindingSchema, StringComparison.Ordinal) &&
            item.Version == SessionBindingVersion)
            ?? throw new InvalidDataException("One LiveKit Audio session binding is required.");
        return value.Value.Deserialize(LiveKitManagedSessionJsonContext.Default.LiveKitAudioSessionBinding)
            ?? throw new InvalidDataException("The LiveKit Audio session binding is invalid.");
    }
}

public sealed record LiveKitManagedAudioSessionBackendOptions
{
    public required string Endpoint { get; init; }

    /// <summary>
    /// Resolves either an LK1 managed credential envelope or an already minted
    /// participant JWT. The returned character array is cleared after use.
    /// </summary>
    public required Func<ManagedAudioSessionStartRequestV1, CancellationToken, ValueTask<char[]>> CredentialResolver { get; init; }

    public required IManagedAudioTranscriptSourceV1 TranscriptSource { get; init; }

    /// <summary>
    /// PCM sample rate shared by the subscribed room stream and the published
    /// Agent track. The 16 kHz default matches the managed STT/TTS PCM path and
    /// avoids an implicit, lossy resampling step.
    /// </summary>
    public int AudioSampleRateHz { get; init; } = 16_000;

    public LiveKitTransportProviderConfig Transport { get; init; } = new();
    public int InboundFrameCapacity { get; init; } = 64;
    public bool VerifyNativeArtifact { get; init; } = true;
}

[JsonSerializable(typeof(LiveKitAudioSessionBinding))]
internal sealed partial class LiveKitManagedSessionJsonContext : JsonSerializerContext;
