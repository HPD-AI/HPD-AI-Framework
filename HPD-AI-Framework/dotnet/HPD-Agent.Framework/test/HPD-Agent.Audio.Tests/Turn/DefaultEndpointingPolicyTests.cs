// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Eot;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Turn;
using Xunit;

namespace HPD.Agent.Audio.Tests.Turn;

public sealed class DefaultEndpointingPolicyTests
{
    [Fact]
    public void Decide_UsesHighConfidenceReasonAndMinDelay()
    {
        var policy = new DefaultEndpointingPolicy(new TurnControllerOptions
        {
            MinEndpointingDelay = TimeSpan.FromMilliseconds(300),
            EotDetector = new HeuristicEotDetector()
        });

        var decision = policy.Decide(CreateContext("Hello there."));

        Assert.False(decision.ShouldCommitNow);
        Assert.Equal(TimeSpan.FromMilliseconds(300), decision.Delay);
        Assert.Equal(0.9f, decision.EotProbability);
        Assert.Equal(EndpointingReason.EotHighConfidence, decision.Reason);
    }

    [Fact]
    public void Decide_CommitsImmediately_WhenMinDelayIsZero()
    {
        var policy = new DefaultEndpointingPolicy(new TurnControllerOptions
        {
            MinEndpointingDelay = TimeSpan.Zero,
            EotDetector = new HeuristicEotDetector()
        });

        var decision = policy.Decide(CreateContext("Commit now."));

        Assert.True(decision.ShouldCommitNow);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
        Assert.Equal(EndpointingReason.EotHighConfidence, decision.Reason);
    }

    [Fact]
    public void Decide_UsesMaxDelay_WhenEotIsUnlikely()
    {
        var policy = new DefaultEndpointingPolicy(new TurnControllerOptions
        {
            MaxEndpointingDelay = TimeSpan.FromMilliseconds(1500),
            EotDetector = new HeuristicEotDetector()
        });

        var decision = policy.Decide(CreateContext("I was thinking"));

        Assert.Equal(TimeSpan.FromMilliseconds(1500), decision.Delay);
        Assert.Equal(EndpointingReason.EotUnlikelyMaxDelay, decision.Reason);
    }

    [Fact]
    public void Decide_UsesFallbackReason_WhenNoDetectorIsConfigured()
    {
        var policy = new DefaultEndpointingPolicy(new TurnControllerOptions
        {
            MinEndpointingDelay = TimeSpan.FromMilliseconds(250)
        });

        var decision = policy.Decide(CreateContext("Anything", EndpointingReason.FinalTranscriptNoSpeech));

        Assert.Equal(TimeSpan.FromMilliseconds(250), decision.Delay);
        Assert.Null(decision.EotProbability);
        Assert.Equal(EndpointingReason.FinalTranscriptNoSpeech, decision.Reason);
    }

    [Theory]
    [InlineData(EndpointingMode.Vad)]
    [InlineData(EndpointingMode.Stt)]
    public void Decide_UsesMinDelayAndFallbackReason_ForNonHybridModes(EndpointingMode mode)
    {
        var policy = new DefaultEndpointingPolicy(new TurnControllerOptions
        {
            Mode = mode,
            MinEndpointingDelay = TimeSpan.FromMilliseconds(200),
            MaxEndpointingDelay = TimeSpan.FromMilliseconds(900),
            EotDetector = new HeuristicEotDetector()
        });

        var decision = policy.Decide(CreateContext("I was thinking", EndpointingReason.SttFinalMinDelay));

        Assert.False(decision.ShouldCommitNow);
        Assert.Equal(TimeSpan.FromMilliseconds(200), decision.Delay);
        Assert.Null(decision.EotProbability);
        Assert.Equal(EndpointingReason.SttFinalMinDelay, decision.Reason);
    }

    [Theory]
    [InlineData(EndpointingMode.Manual)]
    [InlineData(EndpointingMode.RealtimeModel)]
    public void Decide_DoesNotAutoCommit_ForExternallyCommittedModes(EndpointingMode mode)
    {
        var policy = new DefaultEndpointingPolicy(new TurnControllerOptions
        {
            Mode = mode,
            MinEndpointingDelay = TimeSpan.Zero,
            EotDetector = new HeuristicEotDetector()
        });

        var decision = policy.Decide(CreateContext("Done.", EndpointingReason.RealtimeModelCommit));

        Assert.False(decision.ShouldCommitNow);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
        Assert.Null(decision.EotProbability);
        Assert.Equal(EndpointingReason.RealtimeModelCommit, decision.Reason);
    }

    private static EndpointingPolicyContext CreateContext(
        string text,
        string fallbackReason = EndpointingReason.VadEndMinDelay) =>
        new()
        {
            Transcript = new SpeechRecognitionTranscript(text, TranscriptRevisionId: "rev-1"),
            State = TurnControllerState.UserMaybeDone,
            FallbackReason = fallbackReason,
            IsSpeaking = false
        };
}
