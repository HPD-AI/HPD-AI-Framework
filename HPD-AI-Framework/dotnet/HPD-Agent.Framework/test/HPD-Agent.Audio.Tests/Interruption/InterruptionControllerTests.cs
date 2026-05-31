// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio;
using HPD.Agent.Audio.Interruption;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Recognition;
using Xunit;

namespace HPD.Agent.Audio.Tests.Interruption;

public sealed class InterruptionControllerTests
{
    [Fact]
    public void Process_SpeechStartedDuringPlayback_PausesForRecovery()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new InterruptionController(new InterruptionControllerOptions
        {
            EnableFalseInterruptionRecovery = true
        });
        controller.Process(CreatePlaybackStarted(observedAt));

        var decision = controller.Process(new SpeechRecognitionStartedEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(100))
        });

        Assert.Equal(InterruptionDecisionState.PotentialInterruption, decision.State);
        Assert.Equal(InterruptionAction.PauseOutput, decision.Action);
        Assert.Equal(InterruptionReason.UserSpeechDuringPlayback, decision.Reason);
        Assert.Equal("speech-1", decision.OutputContext?.SpeechId);
    }

    [Fact]
    public void Process_MeaningfulTranscriptDuringPausedOutput_Interrupts()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new InterruptionController();
        controller.Process(CreatePlaybackStarted(observedAt));
        controller.Process(new SpeechRecognitionStartedEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(100))
        });

        var decision = controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(200)),
            Transcript = new SpeechRecognitionTranscript("Actually stop there", TranscriptRevisionId: "rev-1")
        });

        Assert.Equal(InterruptionDecisionState.ConfirmedInterruption, decision.State);
        Assert.Equal(InterruptionAction.InterruptOutput, decision.Action);
        Assert.Equal(InterruptionReason.MeaningfulSpeech, decision.Reason);
    }

    [Fact]
    public void Process_ShortTranscriptDuringPausedOutput_Resumes()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new InterruptionController(new InterruptionControllerOptions
        {
            BackchannelStrategy = BackchannelStrategy.IgnoreShortUtterances,
            MinWordsForInterruption = 2,
            ResumeFalseInterruption = true
        });
        controller.Process(CreatePlaybackStarted(observedAt));
        controller.Process(new SpeechRecognitionStartedEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(100))
        });

        var decision = controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(200)),
            Transcript = new SpeechRecognitionTranscript("yeah", TranscriptRevisionId: "rev-1")
        });

        Assert.Equal(InterruptionDecisionState.PotentialBackchannel, decision.State);
        Assert.Equal(InterruptionAction.ResumeOutput, decision.Action);
        Assert.Equal(InterruptionReason.ShortBackchannel, decision.Reason);
    }

    [Fact]
    public void AdvanceFalseInterruption_ResumesAfterTimeout()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new InterruptionController(new InterruptionControllerOptions
        {
            FalseInterruptionTimeout = TimeSpan.FromSeconds(2),
            ResumeFalseInterruption = true
        });
        controller.Process(CreatePlaybackStarted(observedAt));
        controller.Process(new SpeechRecognitionStartedEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(100))
        });

        var early = controller.AdvanceFalseInterruption(observedAt.AddSeconds(1));
        var timedOut = controller.AdvanceFalseInterruption(observedAt.AddMilliseconds(2100));

        Assert.Equal(InterruptionAction.None, early.Action);
        Assert.Equal(InterruptionDecisionState.FalseInterruption, timedOut.State);
        Assert.Equal(InterruptionAction.ResumeOutput, timedOut.Action);
        Assert.Equal(InterruptionReason.FalseInterruptionTimeout, timedOut.Reason);
    }

    [Fact]
    public void Process_KnownBackchannel_ResumesWhenConfigured()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new InterruptionController(new InterruptionControllerOptions
        {
            BackchannelStrategy = BackchannelStrategy.IgnoreKnownBackchannels
        });
        controller.Process(CreatePlaybackStarted(observedAt));
        controller.Process(new SpeechRecognitionStartedEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(100))
        });

        var decision = controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateRecognitionContext(observedAt.AddMilliseconds(200)),
            Transcript = new SpeechRecognitionTranscript("uh-huh", TranscriptRevisionId: "rev-1")
        });

        Assert.Equal(InterruptionAction.ResumeOutput, decision.Action);
        Assert.Equal(InterruptionReason.KnownBackchannel, decision.Reason);
    }

    private static SpeechOutputPlaybackStartedEvent CreatePlaybackStarted(DateTimeOffset observedAt) =>
        new()
        {
            Context = new SpeechOutputContext(
                RuntimeId: "runtime-1",
                SessionId: "session-1",
                BranchId: "main",
                SpeechId: "speech-1",
                StreamId: "stream-1",
                OutputId: "synth-1",
                Provider: "test",
                Model: "tts",
                Voice: "voice",
                SequenceNumber: 1,
                TimestampNs: 123,
                ObservedAt: observedAt),
            State = new SpeechOutputState()
        };

    private static SpeechRecognitionContext CreateRecognitionContext(DateTimeOffset observedAt) =>
        new(
            RuntimeId: "runtime-1",
            SessionId: "session-1",
            BranchId: "main",
            UtteranceId: "utt-1",
            RecognitionId: "rec-1",
            SegmentId: "seg-1",
            ProviderRequestId: "provider-request-1",
            Provider: "test",
            Model: "stt",
            SequenceNumber: 7,
            TimestampNs: 123,
            ObservedAt: observedAt);
}
