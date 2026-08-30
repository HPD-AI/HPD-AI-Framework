using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModuleManifest(
    "hpd.agent.audio",
    typeof(HPD.Agent.Audio.AgentIntegration.Output.AudioAgentEventModule),
    typeof(CoreAgentEventModule))]

namespace HPD.Agent.Audio.AgentIntegration.Output;

/// <summary>Immutable durable event declarations owned by HPD Agent Audio.</summary>
public static class AudioAgentEventModule
{
    public const string AssistantAudioOutputFailed = "ASSISTANT_AUDIO_OUTPUT_FAILED";
    public const string AssistantAudioOutputStarted = "ASSISTANT_AUDIO_OUTPUT_STARTED";
    public const string AssistantAudioOutputStreamStarted = "ASSISTANT_AUDIO_OUTPUT_STREAM_STARTED";
    public const string AssistantAudioOutputChunkReady = "ASSISTANT_AUDIO_OUTPUT_CHUNK_READY";
    public const string AssistantAudioPushTextStreamOpening = "ASSISTANT_AUDIO_PUSH_TEXT_STREAM_OPENING";
    public const string AssistantAudioPushTextStreamOpened = "ASSISTANT_AUDIO_PUSH_TEXT_STREAM_OPENED";
    public const string AssistantAudioPushTextInputSent = "ASSISTANT_AUDIO_PUSH_TEXT_INPUT_SENT";
    public const string AssistantAudioOutputStreamCompleted = "ASSISTANT_AUDIO_OUTPUT_STREAM_COMPLETED";
    public const string AssistantAudioOutputArtifactCaptured = "ASSISTANT_AUDIO_OUTPUT_ARTIFACT_CAPTURED";
    public const string AssistantAudioOutputSegmentFailed = "ASSISTANT_AUDIO_OUTPUT_SEGMENT_FAILED";
    public const string AssistantAudioOutputCompleted = "ASSISTANT_AUDIO_OUTPUT_COMPLETED";
    public const string AssistantAudioPlaybackQueued = "ASSISTANT_AUDIO_PLAYBACK_QUEUED";
    public const string AssistantAudioPlaybackStarted = "ASSISTANT_AUDIO_PLAYBACK_STARTED";
    public const string AssistantAudioPlaybackProgress = "ASSISTANT_AUDIO_PLAYBACK_PROGRESS";
    public const string AssistantAudioPlaybackCompleted = "ASSISTANT_AUDIO_PLAYBACK_COMPLETED";
    public const string AssistantAudioPlaybackInterrupted = "ASSISTANT_AUDIO_PLAYBACK_INTERRUPTED";
    public const string AssistantAudioPlaybackFailed = "ASSISTANT_AUDIO_PLAYBACK_FAILED";

    /// <summary>Gets the immutable audio event fragment.</summary>
    public static AgentEventModuleFragment Fragment { get; } = new()
    {
        ModuleId = "hpd.agent.audio",
        Events = Array.AsReadOnly<AgentEventDescriptor>(
        [
        ])
    };

    private static AgentEventDescriptor Create(Type type, string discriminator, JsonTypeInfo typeInfo) => new()
    {
        EventType = type,
        Discriminator = discriminator,
        JsonTypeInfo = typeInfo,
        Durability = AgentEventDurability.Durable,
        ModuleId = "hpd.agent.audio"
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(AssistantAudioOutputFailedEvent))]
[JsonSerializable(typeof(AssistantAudioOutputStartedEvent))]
[JsonSerializable(typeof(AssistantAudioOutputStreamStartedEvent))]
[JsonSerializable(typeof(AssistantAudioOutputChunkReadyEvent))]
[JsonSerializable(typeof(AssistantAudioPushTextStreamOpeningEvent))]
[JsonSerializable(typeof(AssistantAudioPushTextStreamOpenedEvent))]
[JsonSerializable(typeof(AssistantAudioPushTextInputSentEvent))]
[JsonSerializable(typeof(AssistantAudioOutputStreamCompletedEvent))]
[JsonSerializable(typeof(AssistantAudioOutputArtifactCapturedEvent))]
[JsonSerializable(typeof(AssistantAudioOutputSegmentFailedEvent))]
[JsonSerializable(typeof(AssistantAudioOutputCompletedEvent))]
[JsonSerializable(typeof(AssistantAudioPlaybackQueuedEvent))]
[JsonSerializable(typeof(AssistantAudioPlaybackStartedEvent))]
[JsonSerializable(typeof(AssistantAudioPlaybackProgressEvent))]
[JsonSerializable(typeof(AssistantAudioPlaybackCompletedEvent))]
[JsonSerializable(typeof(AssistantAudioPlaybackInterruptedEvent))]
[JsonSerializable(typeof(AssistantAudioPlaybackFailedEvent))]
[JsonSerializable(typeof(AudioArtifactRef))]
[JsonSerializable(typeof(AudioErrorInfo))]
internal sealed partial class AssistantAudioOutputEventJsonContext : JsonSerializerContext;
