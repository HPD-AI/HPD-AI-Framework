using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Output;
using HPD.Agent.Serialization;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class AssistantAudioOutputEventSerializationTests
{
    private static readonly AgentEventCodec Codec = AgentEventComposition.Create([
        CoreAgentEventModule.Fragment,
        GeneratedAgentEventModule_HPD_Agent_Audio_1448db00.Fragment
    ]).Codec;
    private const string SessionId = "session-audio";
    private const string OutputFlowId = "output-flow";
    private const string ResponseId = "response-audio";
    private const string SegmentId = "segment-audio";
    private const string ProviderKey = "fake-tts";
    private const string ModelId = "model-audio";
    private const string VoiceId = "voice-audio";
    private const string Language = "en";
    private const string OutputFormat = "pcm";
    private const string MediaType = "audio/pcm";
    private const string PayloadKind = "DecodedPcmFrame";

    [Fact]
    public void OutputLifecycleEvents_RoundTripValuesThroughApplicationCodec()
    {
        var started = RoundTrip(new AssistantAudioOutputStartedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat));

        Assert.Equal(OutputFlowId, started.OutputFlowId);
        Assert.Equal(ResponseId, started.ResponseId);
        Assert.Equal(ProviderKey, started.ProviderKey);
        Assert.Equal(ModelId, started.ModelId);
        Assert.Equal(VoiceId, started.VoiceId);
        Assert.Equal(Language, started.Language);
        Assert.Equal(OutputFormat, started.OutputFormat);

        var completed = RoundTrip(new AssistantAudioOutputCompletedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            "Completed",
            SegmentCount: 2,
            Played: true,
            HeardByUser: true));

        Assert.Equal("Completed", completed.Disposition);
        Assert.Equal(2, completed.SegmentCount);
        Assert.True(completed.Played);
        Assert.True(completed.HeardByUser);

        var failed = RoundTrip(new AssistantAudioOutputFailedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat,
            NewError("output_failed"),
            "Failed"));

        Assert.Equal(ProviderKey, failed.ProviderKey);
        Assert.Equal("output_failed", failed.Error?.Code);
        Assert.Equal("Failed", failed.Disposition);
    }

    [Fact]
    public void OutputStreamEvents_RoundTripValuesThroughApplicationCodec()
    {
        var streamStarted = RoundTrip(new AssistantAudioOutputStreamStartedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat,
            MediaType,
            PayloadKind));

        Assert.Equal(SegmentId, streamStarted.SegmentId);
        Assert.Equal(3, streamStarted.SegmentSequence);
        Assert.Equal(MediaType, streamStarted.MediaType);
        Assert.Equal(PayloadKind, streamStarted.PayloadKind);

        var chunkReady = RoundTrip(new AssistantAudioOutputChunkReadyEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            ChunkSequence: 7,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat,
            MediaType,
            SizeBytes: 640,
            Duration: TimeSpan.FromMilliseconds(20),
            IsFinalChunk: true,
            PayloadKind));

        Assert.Equal(7, chunkReady.ChunkSequence);
        Assert.Equal(640, chunkReady.SizeBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(20), chunkReady.Duration);
        Assert.True(chunkReady.IsFinalChunk);

        var streamCompleted = RoundTrip(new AssistantAudioOutputStreamCompletedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            "Completed",
            ChunkCount: 8,
            SizeBytes: 1024,
            TimeSpan.FromMilliseconds(160)));

        Assert.Equal("Completed", streamCompleted.Disposition);
        Assert.Equal(8, streamCompleted.ChunkCount);
        Assert.Equal(1024, streamCompleted.SizeBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(160), streamCompleted.Duration);

        var artifact = new AudioArtifactRef(
            "content-store",
            "artifact-1",
            MediaType,
            SizeBytes: 1024,
            Sha256: "abc123");

        var captured = RoundTrip(new AssistantAudioOutputArtifactCapturedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            MediaType,
            artifact,
            SizeBytes: 1024,
            Sha256: "abc123",
            Duration: TimeSpan.FromMilliseconds(160)));

        Assert.Equal(artifact, captured.Artifact);
        Assert.Equal("abc123", captured.Sha256);

        var segmentFailed = RoundTrip(new AssistantAudioOutputSegmentFailedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat,
            NewError("segment_failed"),
            "Skipped",
            IsFinal: false));

        Assert.Equal("segment_failed", segmentFailed.Error?.Code);
        Assert.Equal("Skipped", segmentFailed.Disposition);
        Assert.False(segmentFailed.IsFinal);
    }

    [Fact]
    public void PushTextEvents_RoundTripValuesThroughApplicationCodec()
    {
        var opening = RoundTrip(new AssistantAudioPushTextStreamOpeningEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat,
            "ProviderDefault"));

        Assert.Equal("ProviderDefault", opening.InputAggregationMode);

        var opened = RoundTrip(new AssistantAudioPushTextStreamOpenedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            ProviderKey,
            ModelId,
            VoiceId,
            Language,
            OutputFormat,
            "ProviderDefault"));

        Assert.Equal(ProviderKey, opened.ProviderKey);
        Assert.Equal("ProviderDefault", opened.InputAggregationMode);

        var inputSent = RoundTrip(new AssistantAudioPushTextInputSentEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SourceTextStart: 4,
            SourceTextLength: 12,
            IsFinalInput: true,
            InputAggregationMode: "ProviderDefault"));

        Assert.Equal(4, inputSent.SourceTextStart);
        Assert.Equal(12, inputSent.SourceTextLength);
        Assert.True(inputSent.IsFinalInput);
        Assert.Equal("ProviderDefault", inputSent.InputAggregationMode);
    }

    [Fact]
    public void PlaybackEvents_RoundTripValuesThroughApplicationCodec()
    {
        var queued = RoundTrip(new AssistantAudioPlaybackQueuedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            MediaType,
            Played: false,
            HeardByUser: false));

        Assert.Equal(MediaType, queued.MediaType);
        Assert.False(queued.Played);
        Assert.False(queued.HeardByUser);

        var started = RoundTrip(new AssistantAudioPlaybackStartedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            MediaType));

        Assert.Equal(SegmentId, started.SegmentId);
        Assert.Equal(3, started.SegmentSequence);

        var progress = RoundTrip(new AssistantAudioPlaybackProgressEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            TimeSpan.FromMilliseconds(250),
            PlayedTextLength: 8,
            "LocalOnly",
            Played: true,
            HeardByUser: false));

        Assert.Equal(TimeSpan.FromMilliseconds(250), progress.PlayedDuration);
        Assert.Equal(8, progress.PlayedTextLength);
        Assert.Equal("LocalOnly", progress.Precision);
        Assert.True(progress.Played);
        Assert.False(progress.HeardByUser);

        var completed = RoundTrip(new AssistantAudioPlaybackCompletedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            MediaType,
            Played: true,
            HeardByUser: true,
            TimeSpan.FromMilliseconds(500),
            PlayedTextLength: 16,
            "LocalOnly"));

        Assert.Equal(TimeSpan.FromMilliseconds(500), completed.Duration);
        Assert.Equal(16, completed.PlayedTextLength);
        Assert.True(completed.HeardByUser);

        var interrupted = RoundTrip(new AssistantAudioPlaybackInterruptedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            TimeSpan.FromMilliseconds(300),
            PlayedTextLength: 10,
            "LocalOnly",
            Played: true,
            HeardByUser: true));

        Assert.Equal(TimeSpan.FromMilliseconds(300), interrupted.PlayedDuration);
        Assert.Equal(10, interrupted.PlayedTextLength);

        var failed = RoundTrip(new AssistantAudioPlaybackFailedEvent(
            SessionId,
            OutputFlowId,
            ResponseId,
            SegmentId,
            SegmentSequence: 3,
            NewError("playback_failed"),
            Played: false,
            HeardByUser: false));

        Assert.Equal("playback_failed", failed.Error.Code);
        Assert.False(failed.Played);
        Assert.False(failed.HeardByUser);
    }

    private static T RoundTrip<T>(T evt)
        where T : AgentEvent
    {
        var json = Codec.Serialize(evt);

        Assert.Contains($"\"sessionId\":\"{SessionId}\"", json);

        var roundTripped = Assert.IsType<T>(Codec.DeserializeEvent(json));
        Assert.Equal(SessionId, roundTripped.SessionId);
        Assert.Equal(OutputFlowId, GetProperty<string>(roundTripped, nameof(AssistantAudioOutputStartedEvent.OutputFlowId)));
        Assert.Equal(ResponseId, GetProperty<string>(roundTripped, nameof(AssistantAudioOutputStartedEvent.ResponseId)));

        return roundTripped;
    }

    private static T? GetProperty<T>(object instance, string propertyName)
    {
        return (T?)instance.GetType().GetProperty(propertyName)?.GetValue(instance);
    }

    private static AudioErrorInfo NewError(string code) =>
        new()
        {
            Code = code,
            Message = $"{code} message",
            Category = "Audio",
            IsRetryable = true
        };
}
