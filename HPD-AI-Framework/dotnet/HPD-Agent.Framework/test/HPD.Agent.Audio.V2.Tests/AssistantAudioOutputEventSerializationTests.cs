using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Serialization;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class AssistantAudioOutputEventSerializationTests
{
    [Fact]
    public void OutputEvents_RoundTripThroughAgentEventSerializer()
    {
        AgentEvent[] events =
        [
            new AssistantAudioOutputStreamStartedEvent(
                "session-output",
                "output-stream",
                "response-output",
                "segment-output",
                SegmentSequence: 0,
                "fake-tts",
                "model",
                "voice",
                "en",
                "pcm",
                "audio/pcm",
                "DecodedPcmFrame"),
            new AssistantAudioOutputChunkReadyEvent(
                "session-output",
                "output-stream",
                "response-output",
                "segment-output",
                SegmentSequence: 0,
                ChunkSequence: 1,
                "fake-tts",
                "model",
                "voice",
                "en",
                "pcm",
                "audio/pcm",
                SizeBytes: 640,
                Duration: TimeSpan.FromMilliseconds(20),
                IsFinalChunk: true,
                PayloadKind: "DecodedPcmFrame"),
            new AssistantAudioPushTextStreamOpeningEvent(
                "session-output",
                "output-stream",
                "response-output",
                "fake-tts",
                "model",
                "voice",
                "en",
                "pcm",
                "ProviderDefault"),
            new AssistantAudioPushTextStreamOpenedEvent(
                "session-output",
                "output-stream",
                "response-output",
                "fake-tts",
                "model",
                "voice",
                "en",
                "pcm",
                "ProviderDefault"),
            new AssistantAudioPushTextInputSentEvent(
                "session-output",
                "output-stream",
                "response-output",
                SourceTextStart: 0,
                SourceTextLength: 12,
                IsFinalInput: false,
                InputAggregationMode: "ProviderDefault"),
            new AssistantAudioOutputStreamCompletedEvent(
                "session-output",
                "output-stream",
                "response-output",
                "segment-output",
                SegmentSequence: 0,
                "Completed",
                ChunkCount: 1,
                SizeBytes: 640,
                TimeSpan.FromMilliseconds(20))
        ];

        foreach (var evt in events)
        {
            var json = AgentEventSerializer.ToJson(evt);
            var roundTripped = AgentEventSerializer.FromJson(json);

            Assert.IsType(evt.GetType(), roundTripped);
        }
    }

    [Fact]
    public void PlaybackEvents_RoundTripThroughAgentEventSerializer()
    {
        var error = new AudioErrorInfo
        {
            Code = "sink_failed",
            Message = "Sink failed.",
            Category = "Playback"
        };
        AgentEvent[] events =
        [
            new AssistantAudioPlaybackQueuedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-playback",
                SegmentSequence: 0,
                "audio/mpeg",
                Played: false,
                HeardByUser: false),
            new AssistantAudioPlaybackStartedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-playback",
                SegmentSequence: 0,
                "audio/mpeg"),
            new AssistantAudioPlaybackProgressEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-playback",
                SegmentSequence: 0,
                TimeSpan.FromMilliseconds(250),
                PlayedTextLength: 8,
                "LocalOnly",
                Played: false,
                HeardByUser: false),
            new AssistantAudioPlaybackCompletedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-playback",
                SegmentSequence: 0,
                "audio/mpeg",
                Played: true,
                HeardByUser: true,
                TimeSpan.FromMilliseconds(500),
                PlayedTextLength: 16,
                "LocalOnly"),
            new AssistantAudioPlaybackStartedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-stream",
                SegmentSequence: 1,
                "audio/pcm"),
            new AssistantAudioPlaybackCompletedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-stream",
                SegmentSequence: 1,
                "audio/pcm",
                Played: true,
                HeardByUser: true,
                TimeSpan.FromMilliseconds(20),
                PlayedTextLength: 6,
                "LocalOnly"),
            new AssistantAudioPlaybackInterruptedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-playback",
                SegmentSequence: 0,
                TimeSpan.FromMilliseconds(250),
                PlayedTextLength: 8,
                "LocalOnly",
                Played: true,
                HeardByUser: true),
            new AssistantAudioPlaybackFailedEvent(
                "session-playback",
                "output-playback",
                "response-playback",
                "segment-playback",
                SegmentSequence: 0,
                error,
                Played: false,
                HeardByUser: false)
        ];

        foreach (var evt in events)
        {
            var json = AgentEventSerializer.ToJson(evt);
            var roundTripped = AgentEventSerializer.FromJson(json);

            Assert.IsType(evt.GetType(), roundTripped);
        }
    }
}
