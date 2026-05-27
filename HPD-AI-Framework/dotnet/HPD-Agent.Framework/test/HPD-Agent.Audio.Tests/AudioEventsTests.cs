// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Preemptive;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Serialization;
using HPD.Agent.Audio.Turn;
using HPD.Events;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Tests for audio event types.
/// </summary>
public class AudioEventsTests
{
    [Fact]
    public void SynthesisStartedEvent_CanBeCreated()
    {
        // Act
        var evt = new SynthesisStartedEvent("synth-123", "tts-1", "nova");

        // Assert
        Assert.Equal("synth-123", evt.SynthesisId);
        Assert.Equal("tts-1", evt.ModelId);
        Assert.Equal("nova", evt.Voice);
    }

    [Fact]
    public void AudioChunkEvent_CanBeCreated()
    {
        // Act
        var evt = new AudioChunkEvent(
            "synth-123",
            Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            "audio/mpeg",
            0,
            TimeSpan.FromMilliseconds(100),
            false);

        // Assert
        Assert.Equal("synth-123", evt.SynthesisId);
        Assert.NotEmpty(evt.Base64Audio);
        Assert.Equal("audio/mpeg", evt.MimeType);
        Assert.Equal(0, evt.ChunkIndex);
        Assert.Equal(TimeSpan.FromMilliseconds(100), evt.Duration);
        Assert.False(evt.IsLast);
    }

    [Fact]
    public void SynthesisCompletedEvent_CanBeCreated()
    {
        // Act
        var evt = new SynthesisCompletedEvent("synth-123", true, 10, 8);

        // Assert
        Assert.Equal("synth-123", evt.SynthesisId);
        Assert.True(evt.WasInterrupted);
        Assert.Equal(10, evt.TotalChunks);
        Assert.Equal(8, evt.DeliveredChunks);
    }

    [Fact]
    public void TranscriptionDeltaEvent_CanBeCreated()
    {
        // Act
        var evt = new TranscriptionDeltaEvent("trans-123", "Hello world", false, 0.95f);

        // Assert
        Assert.Equal("trans-123", evt.TranscriptionId);
        Assert.Equal("Hello world", evt.Text);
        Assert.False(evt.IsFinal);
        Assert.Equal(0.95f, evt.Confidence);
    }

    [Fact]
    public void TranscriptionCompletedEvent_CanBeCreated()
    {
        // Act
        var evt = new TranscriptionCompletedEvent(
            "trans-123",
            "Hello world!",
            TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.Equal("trans-123", evt.TranscriptionId);
        Assert.Equal("Hello world!", evt.FinalText);
        Assert.Equal(TimeSpan.FromMilliseconds(500), evt.ProcessingDuration);
    }

    [Fact]
    public void AudioInputFrame_WithSequenceNumber_ReturnsSequencedCopy()
    {
        var frame = new AudioInputFrame(
            SessionId: "session-1",
            BranchId: "main",
            Audio: new byte[] { 1, 2, 3 },
            MimeType: "audio/pcm",
            TimestampNs: 123,
            IsFinal: false);

        var sequenced = frame.WithSequenceNumber(42);

        Assert.Equal(0, frame.SequenceNumber);
        Assert.Equal(42, sequenced.SequenceNumber);
        Assert.Equal(frame.Audio, sequenced.Audio);
    }

    [Fact]
    public void SpeechRecognitionFinalEvent_CanBeCreated()
    {
        var context = CreateRecognitionContext();
        var transcript = new SpeechRecognitionTranscript(
            Text: "Hello world",
            Confidence: 0.94f,
            Language: "en",
            TranscriptRevisionId: "rev-1");

        var evt = new SpeechRecognitionFinalEvent
        {
            Context = context,
            Transcript = transcript
        };

        Assert.Equal(context, evt.Context);
        Assert.Equal("Hello world", evt.Transcript.Text);
        Assert.Equal(EventChannel.Synchronous, evt.Channel);
    }

    [Fact]
    public void SpeechRecognitionEvent_SerializesWithType()
    {
        var evt = new SpeechRecognitionInterimEvent
        {
            Context = CreateRecognitionContext(),
            Transcript = new SpeechRecognitionTranscript("Hel")
        };

        var json = AudioEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"SPEECH_RECOGNITION_INTERIM\"", json);
        Assert.Contains("\"text\":\"Hel\"", json);
    }

    [Fact]
    public void SpeechOutputAudioQueuedEvent_CanBeCreated()
    {
        var context = CreateOutputContext();
        var frame = new AudioChunkFrame(
            SynthesisId: "synth-1",
            Audio: new byte[] { 1, 2, 3 },
            MimeType: "audio/mpeg",
            ChunkIndex: 0,
            Duration: TimeSpan.FromMilliseconds(80),
            IsLast: false,
            TimestampNs: 123,
            SequenceNumber: 7);
        var state = new SpeechOutputState
        {
            GeneratedDuration = frame.Duration,
            QueuedDuration = frame.Duration,
            QueuedChunks = 1,
            EmittedChunks = 1
        };

        var evt = new SpeechOutputAudioQueuedEvent
        {
            Context = context,
            Frame = frame,
            State = state
        };

        Assert.Equal(context, evt.Context);
        Assert.Equal(frame, evt.Frame);
        Assert.Equal(1, evt.State.QueuedChunks);
        Assert.Equal(EventChannel.Streaming, evt.Channel);
    }

    [Fact]
    public void SpeechOutputEvent_SerializesWithType()
    {
        var evt = new SpeechOutputTextQueuedEvent
        {
            Context = CreateOutputContext(),
            Text = "Hello from output"
        };

        var json = AudioEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"SPEECH_OUTPUT_TEXT_QUEUED\"", json);
        Assert.Contains("\"text\":\"Hello from output\"", json);
    }

    [Fact]
    public void UserTurnCommittedEvent_CanBeCreated()
    {
        var transcript = new SpeechRecognitionTranscript(
            Text: "Please continue.",
            Confidence: 0.9f,
            TranscriptRevisionId: "rev-1");
        var evt = new UserTurnCommittedEvent
        {
            Context = CreateTurnContext(),
            Transcript = transcript,
            Reason = EndpointingReason.VadEndMinDelay
        };

        Assert.Equal("turn-1", evt.Context.TurnId);
        Assert.Equal("Please continue.", evt.Transcript.Text);
        Assert.Equal(EndpointingReason.VadEndMinDelay, evt.Reason);
        Assert.Equal(EventChannel.Synchronous, evt.Channel);
    }

    [Fact]
    public void UserTurnEvent_SerializesWithType()
    {
        var evt = new UserTurnReadyEvent
        {
            Context = CreateTurnContext(),
            Transcript = new SpeechRecognitionTranscript("Hello."),
            Decision = new EndpointingDecision
            {
                Delay = TimeSpan.FromMilliseconds(300),
                EotProbability = 0.9f,
                Reason = EndpointingReason.EotHighConfidence
            }
        };

        var json = AudioEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"USER_TURN_READY\"", json);
        Assert.Contains("\"reason\":\"eot_high_confidence\"", json);
    }

    [Fact]
    public void UserInterruptedEvent_CanBeCreated()
    {
        // Act
        var evt = new UserInterruptedEvent("wait, stop");

        // Assert
        Assert.Equal("wait, stop", evt.TranscribedText);
    }

    [Fact]
    public void UserInterruptedEvent_CanHaveNullText()
    {
        // Act
        var evt = new UserInterruptedEvent(null);

        // Assert
        Assert.Null(evt.TranscribedText);
    }

    [Fact]
    public void SpeechPausedEvent_CanBeCreated()
    {
        // Act
        var evt = new SpeechPausedEvent("synth-123", "user_speaking");

        // Assert
        Assert.Equal("synth-123", evt.SynthesisId);
        Assert.Equal("user_speaking", evt.Reason);
    }

    [Fact]
    public void SpeechResumedEvent_CanBeCreated()
    {
        // Act
        var evt = new SpeechResumedEvent("synth-123", TimeSpan.FromSeconds(2.5));

        // Assert
        Assert.Equal("synth-123", evt.SynthesisId);
        Assert.Equal(TimeSpan.FromSeconds(2.5), evt.PauseDuration);
    }

    [Fact]
    public void PreemptiveGenerationStartedEvent_CanBeCreated()
    {
        // Act
        var candidate = new PreemptiveGenerationCandidate
        {
            GenerationId = "gen-123",
            RecognitionId = "rec-1",
            UtteranceId = "utt-1",
            TranscriptRevisionId = "rev-1",
            TranscriptText = "Hello",
            Confidence = 0.85f,
            CreatedAt = DateTimeOffset.UnixEpoch
        };
        var evt = new PreemptiveGenerationStartedEvent(candidate);

        // Assert
        Assert.Equal("gen-123", evt.GenerationId);
        Assert.Equal(0.85f, evt.EndOfTurnProbability);
        Assert.Equal(candidate, evt.Candidate);
    }

    [Fact]
    public void PreemptiveGenerationDiscardedEvent_CanBeCreated()
    {
        // Act
        var evt = new PreemptiveGenerationDiscardedEvent(
            "gen-123",
            PreemptiveGenerationReason.UserContinued);

        // Assert
        Assert.Equal("gen-123", evt.GenerationId);
        Assert.Equal(PreemptiveGenerationReason.UserContinued, evt.Reason);
    }

    [Fact]
    public void VadStartOfSpeechEvent_CanBeCreated()
    {
        // Act
        var evt = new VadStartOfSpeechEvent(TimeSpan.FromSeconds(1.5), 0.92f);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1.5), evt.AudioTimestamp);
        Assert.Equal(0.92f, evt.SpeechProbability);
    }

    [Fact]
    public void VadEndOfSpeechEvent_CanBeCreated()
    {
        // Act
        var evt = new VadEndOfSpeechEvent(
            TimeSpan.FromSeconds(5.0),
            TimeSpan.FromSeconds(3.5),
            0.15f);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5.0), evt.AudioTimestamp);
        Assert.Equal(TimeSpan.FromSeconds(3.5), evt.SpeechDuration);
        Assert.Equal(0.15f, evt.SpeechProbability);
    }

    [Fact]
    public void AudioPipelineMetricsEvent_CanBeCreated()
    {
        // Act
        var evt = new AudioPipelineMetricsEvent(
            "latency",
            "time_to_first_audio",
            150.5,
            "ms");

        // Assert
        Assert.Equal("latency", evt.MetricType);
        Assert.Equal("time_to_first_audio", evt.MetricName);
        Assert.Equal(150.5, evt.Value);
        Assert.Equal("ms", evt.Unit);
    }

    [Fact]
    public void AudioExperienceMetricEvent_CanBeCreated()
    {
        // Act
        var evt = new AudioExperienceMetricEvent(
            "playback_completion_ratio",
            0.75,
            "ratio",
            SpeechId: "speech-1",
            OutputStreamId: "stream-1");

        // Assert
        Assert.Equal("playback_completion_ratio", evt.MetricName);
        Assert.Equal(0.75, evt.Value);
        Assert.Equal("ratio", evt.Unit);
        Assert.Equal("speech-1", evt.SpeechId);
        Assert.Equal("stream-1", evt.OutputStreamId);
        Assert.Equal(EventKind.Diagnostic, evt.Kind);
        Assert.Equal(EventChannel.Streaming, evt.Channel);
    }

    [Fact]
    public void AudioExperienceMetricEvent_SerializesWithDiscriminator()
    {
        // Arrange
        var evt = new AudioExperienceMetricEvent(
            "audio_played_duration",
            120,
            "ms");

        // Act
        var json = AudioEventSerializer.ToJson(evt);

        // Assert
        Assert.Contains("\"type\":\"AUDIO_EXPERIENCE_METRIC\"", json);
        Assert.Contains("\"metricName\":\"audio_played_duration\"", json);
    }

    [Fact]
    public void EotDetectedEvent_CanBeCreated()
    {
        // Act
        var evt = new EotDetectedEvent(
            "Hello, how are you?",
            0.9f,
            TimeSpan.FromSeconds(0.8),
            "heuristic-eot");

        // Assert
        Assert.Equal("Hello, how are you?", evt.TranscribedText);
        Assert.Equal(0.9f, evt.EndOfTurnProbability);
        Assert.Equal(TimeSpan.FromSeconds(0.8), evt.SilenceDuration);
        Assert.Equal("heuristic-eot", evt.DetectionMethod);
    }

    [Fact]
    public void FillerAudioPlayedEvent_CanBeCreated()
    {
        // Act
        var evt = new FillerAudioPlayedEvent("Um...", TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.Equal("Um...", evt.Phrase);
        Assert.Equal(TimeSpan.FromMilliseconds(500), evt.Duration);
    }

    [Fact]
    public void AudioEvents_InheritFromAgentEvent()
    {
        // Assert all audio events inherit from AgentEvent
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SynthesisStartedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(AudioChunkEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SynthesisCompletedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(TranscriptionDeltaEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(TranscriptionCompletedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(UserInterruptedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SpeechPausedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SpeechResumedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(PreemptiveGenerationStartedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(PreemptiveGenerationDiscardedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(VadStartOfSpeechEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(VadEndOfSpeechEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(AudioPipelineMetricsEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(AudioExperienceMetricEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(EotDetectedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(FillerAudioPlayedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SpeechOutputStartedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SpeechOutputAudioQueuedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(SpeechOutputCompletedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(UserTurnStartedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(UserTurnCommittedEvent)));
    }

    [Fact]
    public void AudioChunkEvent_CanSetStreamingChannelProperties()
    {
        // Act
        var evt = new AudioChunkEvent(
            "synth-123",
            Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            "audio/mpeg",
            0,
            TimeSpan.FromMilliseconds(100),
            false)
        {
            Channel = EventChannel.Streaming,
            EventFlowId = "stream-456",
            CanInterrupt = true
        };

        // Assert
        Assert.Equal(EventChannel.Streaming, evt.Channel);
        Assert.Equal("stream-456", evt.EventFlowId);
        Assert.True(evt.CanInterrupt);
    }

    private static SpeechRecognitionContext CreateRecognitionContext() =>
        new(
            RuntimeId: "runtime-1",
            SessionId: "session-1",
            BranchId: "main",
            UtteranceId: "utt-1",
            RecognitionId: "rec-1",
            SegmentId: "seg-1",
            ProviderRequestId: "provider-request-1",
            Provider: "test",
            Model: "test-model",
            SequenceNumber: 7,
            TimestampNs: 123,
            ObservedAt: DateTimeOffset.UnixEpoch);

    private static SpeechOutputContext CreateOutputContext() =>
        new(
            RuntimeId: "runtime-1",
            SessionId: "session-1",
            BranchId: "main",
            SpeechId: "speech-1",
            StreamId: "stream-1",
            SynthesisId: "synth-1",
            Provider: "test",
            Model: "test-model",
            Voice: "voice-1",
            SequenceNumber: 7,
            TimestampNs: 123,
            ObservedAt: DateTimeOffset.UnixEpoch);

    private static UserTurnContext CreateTurnContext() =>
        new(
            RuntimeId: "runtime-1",
            SessionId: "session-1",
            BranchId: "main",
            TurnId: "turn-1",
            UtteranceId: "utt-1",
            RecognitionId: "rec-1",
            TranscriptRevisionId: "rev-1",
            SequenceNumber: 7,
            TimestampNs: 123,
            ObservedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void SynthesisCompletedEvent_CanSetControlChannel()
    {
        // Act
        var evt = new SynthesisCompletedEvent("synth-123")
        {
            Channel = EventChannel.Control,
            CanInterrupt = false
        };

        // Assert
        Assert.Equal(EventChannel.Control, evt.Channel);
        Assert.False(evt.CanInterrupt);
    }
}
