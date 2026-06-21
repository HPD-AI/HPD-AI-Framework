using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Serialization;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal static class AssistantAudioOutputEventRegistration
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

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputFailedEvent),
            AssistantAudioOutputFailed,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputFailedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputStartedEvent),
            AssistantAudioOutputStarted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputStartedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputStreamStartedEvent),
            AssistantAudioOutputStreamStarted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputStreamStartedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputChunkReadyEvent),
            AssistantAudioOutputChunkReady,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputChunkReadyEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPushTextStreamOpeningEvent),
            AssistantAudioPushTextStreamOpening,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPushTextStreamOpeningEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPushTextStreamOpenedEvent),
            AssistantAudioPushTextStreamOpened,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPushTextStreamOpenedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPushTextInputSentEvent),
            AssistantAudioPushTextInputSent,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPushTextInputSentEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputStreamCompletedEvent),
            AssistantAudioOutputStreamCompleted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputStreamCompletedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputArtifactCapturedEvent),
            AssistantAudioOutputArtifactCaptured,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputArtifactCapturedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputSegmentFailedEvent),
            AssistantAudioOutputSegmentFailed,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputSegmentFailedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioOutputCompletedEvent),
            AssistantAudioOutputCompleted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioOutputCompletedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPlaybackQueuedEvent),
            AssistantAudioPlaybackQueued,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPlaybackQueuedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPlaybackStartedEvent),
            AssistantAudioPlaybackStarted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPlaybackStartedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPlaybackProgressEvent),
            AssistantAudioPlaybackProgress,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPlaybackProgressEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPlaybackCompletedEvent),
            AssistantAudioPlaybackCompleted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPlaybackCompletedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPlaybackInterruptedEvent),
            AssistantAudioPlaybackInterrupted,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPlaybackInterruptedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(AssistantAudioPlaybackFailedEvent),
            AssistantAudioPlaybackFailed,
            AssistantAudioOutputEventJsonContext.Default.AssistantAudioPlaybackFailedEvent);
    }
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
