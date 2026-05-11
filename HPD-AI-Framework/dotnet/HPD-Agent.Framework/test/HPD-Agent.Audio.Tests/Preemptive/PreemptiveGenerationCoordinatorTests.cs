// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Preemptive;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Turn;
using Xunit;

namespace HPD.Agent.Audio.Tests.Preemptive;

public sealed class PreemptiveGenerationCoordinatorTests
{
    [Fact]
    public void TryStart_CreatesCandidateFromPreflightTranscript()
    {
        var coordinator = new PreemptiveGenerationCoordinator(new PreemptiveGenerationOptions
        {
            ConfidenceThreshold = 0.7f
        });

        var started = coordinator.TryStart(
            CreatePreflight("Hello there.", confidence: 0.9f),
            chatContextFingerprint: "chat-1",
            toolSetFingerprint: "tools-1");

        Assert.NotNull(started);
        Assert.NotNull(coordinator.ActiveCandidate);
        Assert.Equal("rec-1", started.Candidate.RecognitionId);
        Assert.Equal("utt-1", started.Candidate.UtteranceId);
        Assert.Equal("rev-1", started.Candidate.TranscriptRevisionId);
        Assert.Equal("Hello there.", started.Candidate.TranscriptText);
        Assert.Equal("chat-1", started.Candidate.ChatContextFingerprint);
        Assert.Equal("tools-1", started.Candidate.ToolSetFingerprint);
    }

    [Fact]
    public void TryStart_IgnoresLowConfidenceTranscript()
    {
        var coordinator = new PreemptiveGenerationCoordinator(new PreemptiveGenerationOptions
        {
            ConfidenceThreshold = 0.8f
        });

        var started = coordinator.TryStart(CreatePreflight("maybe", confidence: 0.4f));

        Assert.Null(started);
        Assert.Null(coordinator.ActiveCandidate);
    }

    [Fact]
    public void EvaluateCommit_ReusesCandidate_WhenTranscriptAndFingerprintsMatch()
    {
        var coordinator = new PreemptiveGenerationCoordinator();
        coordinator.TryStart(
            CreatePreflight("Hello there.", confidence: 0.9f),
            chatContextFingerprint: "chat-1",
            toolSetFingerprint: "tools-1");

        var decision = coordinator.EvaluateCommit(
            CreateCommitted("Hello there.", "rev-1"),
            chatContextFingerprint: "chat-1",
            toolSetFingerprint: "tools-1");

        Assert.True(decision.ReuseCandidate);
        Assert.Equal(PreemptiveGenerationReason.Reuse, decision.Reason);
        Assert.NotNull(decision.Candidate);
        Assert.Null(coordinator.ActiveCandidate);
    }

    [Fact]
    public void EvaluateCommit_DiscardsCandidate_WhenTranscriptChanges()
    {
        var coordinator = new PreemptiveGenerationCoordinator();
        coordinator.TryStart(CreatePreflight("Hello there.", confidence: 0.9f));

        var decision = coordinator.EvaluateCommit(CreateCommitted("Hello there please.", "rev-2"));

        Assert.False(decision.ReuseCandidate);
        Assert.Equal(PreemptiveGenerationReason.TranscriptMismatch, decision.Reason);
        Assert.NotNull(decision.Candidate);
        Assert.Null(coordinator.ActiveCandidate);
    }

    [Fact]
    public void Discard_EmitsCandidateCorrelation()
    {
        var coordinator = new PreemptiveGenerationCoordinator();
        coordinator.TryStart(CreatePreflight("Hello there.", confidence: 0.9f));

        var discarded = coordinator.Discard(PreemptiveGenerationReason.UserContinued);

        Assert.NotNull(discarded);
        Assert.Equal("rec-1", discarded.RecognitionId);
        Assert.Equal("utt-1", discarded.UtteranceId);
        Assert.Equal("rev-1", discarded.TranscriptRevisionId);
        Assert.Equal(PreemptiveGenerationReason.UserContinued, discarded.Reason);
        Assert.Null(coordinator.ActiveCandidate);
    }

    private static SpeechRecognitionPreflightEvent CreatePreflight(string text, float confidence) =>
        new()
        {
            Context = CreateRecognitionContext(),
            Transcript = new SpeechRecognitionTranscript(
                Text: text,
                Confidence: confidence,
                TranscriptRevisionId: "rev-1")
        };

    private static UserTurnCommittedEvent CreateCommitted(string text, string revisionId) =>
        new()
        {
            Context = new UserTurnContext(
                RuntimeId: "runtime-1",
                SessionId: "session-1",
                BranchId: "main",
                TurnId: "turn-1",
                UtteranceId: "utt-1",
                RecognitionId: "rec-1",
                TranscriptRevisionId: revisionId,
                SequenceNumber: 7,
                TimestampNs: 123,
                ObservedAt: DateTimeOffset.UnixEpoch.AddMilliseconds(200)),
            Transcript = new SpeechRecognitionTranscript(text, TranscriptRevisionId: revisionId),
            Reason = EndpointingReason.VadEndMinDelay
        };

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
}
