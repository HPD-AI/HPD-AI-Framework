// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using HPD.Events;
using System.Text.Json;
using Xunit;
using EventPriority = HPD.Events.EventPriority;

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
        var evt = new PreemptiveGenerationStartedEvent("gen-123", 0.85f);

        // Assert
        Assert.Equal("gen-123", evt.GenerationId);
        Assert.Equal(0.85f, evt.TurnCompletionProbability);
    }

    [Fact]
    public void PreemptiveGenerationDiscardedEvent_CanBeCreated()
    {
        // Act
        var evt = new PreemptiveGenerationDiscardedEvent("gen-123", "user_continued");

        // Assert
        Assert.Equal("gen-123", evt.GenerationId);
        Assert.Equal("user_continued", evt.Reason);
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
    public void TurnDetectedEvent_CanBeCreated()
    {
        // Act
        var evt = new TurnDetectedEvent(
            "Hello, how are you?",
            0.9f,
            TimeSpan.FromSeconds(0.8),
            "heuristic");

        // Assert
        Assert.Equal("Hello, how are you?", evt.TranscribedText);
        Assert.Equal(0.9f, evt.CompletionProbability);
        Assert.Equal(TimeSpan.FromSeconds(0.8), evt.SilenceDuration);
        Assert.Equal("heuristic", evt.DetectionMethod);
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
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(TurnDetectedEvent)));
        Assert.True(typeof(AgentEvent).IsAssignableFrom(typeof(FillerAudioPlayedEvent)));
    }

    [Fact]
    public void AudioChunkEvent_CanSetPriorityStreamingProperties()
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
            Priority = EventPriority.Normal,
            StreamId = "stream-456",
            CanInterrupt = true
        };

        // Assert
        Assert.Equal(EventPriority.Normal, evt.Priority);
        Assert.Equal("stream-456", evt.StreamId);
        Assert.True(evt.CanInterrupt);
    }

    [Fact]
    public void SynthesisCompletedEvent_CanSetControlPriority()
    {
        // Act
        var evt = new SynthesisCompletedEvent("synth-123")
        {
            Priority = EventPriority.Control,
            CanInterrupt = false
        };

        // Assert
        Assert.Equal(EventPriority.Control, evt.Priority);
        Assert.False(evt.CanInterrupt);
    }
}
