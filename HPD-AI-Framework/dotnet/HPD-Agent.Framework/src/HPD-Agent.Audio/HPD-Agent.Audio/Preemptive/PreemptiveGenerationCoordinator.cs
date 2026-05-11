// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Turn;

namespace HPD.Agent.Audio.Preemptive;

/// <summary>
/// Tracks speculative generation candidates derived from stable recognition transcripts.
/// </summary>
public sealed class PreemptiveGenerationCoordinator
{
    private readonly PreemptiveGenerationOptions _options;

    /// <summary>Creates a preemptive generation coordinator.</summary>
    public PreemptiveGenerationCoordinator(PreemptiveGenerationOptions? options = null)
    {
        _options = options ?? new PreemptiveGenerationOptions();
    }

    /// <summary>Active speculative candidate, if one exists.</summary>
    public PreemptiveGenerationCandidate? ActiveCandidate { get; private set; }

    /// <summary>Starts a candidate from a stable preflight recognition transcript.</summary>
    public PreemptiveGenerationStartedEvent? TryStart(
        SpeechRecognitionPreflightEvent evt,
        string? chatContextFingerprint = null,
        string? toolSetFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var confidence = evt.Transcript.Confidence ?? 1.0f;
        if (confidence < _options.ConfidenceThreshold)
            return null;

        ActiveCandidate = new PreemptiveGenerationCandidate
        {
            GenerationId = Guid.NewGuid().ToString("N"),
            RecognitionId = evt.Context.RecognitionId,
            UtteranceId = evt.Context.UtteranceId,
            TranscriptRevisionId = evt.Transcript.TranscriptRevisionId ?? Guid.NewGuid().ToString("N"),
            TranscriptText = evt.Transcript.Text,
            ChatContextFingerprint = chatContextFingerprint,
            ToolSetFingerprint = toolSetFingerprint,
            Confidence = confidence,
            CreatedAt = evt.Context.ObservedAt
        };

        return new PreemptiveGenerationStartedEvent(ActiveCandidate);
    }

    /// <summary>Compares the active candidate with a committed user turn.</summary>
    public PreemptiveGenerationCommitDecision EvaluateCommit(
        UserTurnCommittedEvent evt,
        string? chatContextFingerprint = null,
        string? toolSetFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var candidate = ActiveCandidate;
        if (candidate is null)
            return new PreemptiveGenerationCommitDecision { Reason = PreemptiveGenerationReason.NoCandidate };

        if (!StringComparer.Ordinal.Equals(candidate.TranscriptText, evt.Transcript.Text) ||
            !StringComparer.Ordinal.Equals(candidate.TranscriptRevisionId, evt.Transcript.TranscriptRevisionId))
        {
            return DiscardDecision(candidate, PreemptiveGenerationReason.TranscriptMismatch);
        }

        if (!StringComparer.Ordinal.Equals(candidate.ChatContextFingerprint, chatContextFingerprint))
            return DiscardDecision(candidate, PreemptiveGenerationReason.ContextMismatch);

        if (!StringComparer.Ordinal.Equals(candidate.ToolSetFingerprint, toolSetFingerprint))
            return DiscardDecision(candidate, PreemptiveGenerationReason.ToolSetMismatch);

        ActiveCandidate = null;
        return new PreemptiveGenerationCommitDecision
        {
            ReuseCandidate = true,
            Candidate = candidate,
            Reason = PreemptiveGenerationReason.Reuse
        };
    }

    /// <summary>Discards the active candidate and emits a discard event.</summary>
    public PreemptiveGenerationDiscardedEvent? Discard(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var candidate = ActiveCandidate;
        if (candidate is null)
            return null;

        ActiveCandidate = null;
        return new PreemptiveGenerationDiscardedEvent(candidate.GenerationId, reason)
        {
            RecognitionId = candidate.RecognitionId,
            UtteranceId = candidate.UtteranceId,
            TranscriptRevisionId = candidate.TranscriptRevisionId
        };
    }

    private PreemptiveGenerationCommitDecision DiscardDecision(
        PreemptiveGenerationCandidate candidate,
        string reason)
    {
        ActiveCandidate = null;
        return new PreemptiveGenerationCommitDecision
        {
            Candidate = candidate,
            Reason = reason
        };
    }
}
