// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Preemptive;

/// <summary>
/// Tracks speculative generation started from a stable recognition transcript.
/// </summary>
public sealed record PreemptiveGenerationCandidate
{
    /// <summary>Stable candidate/generation identifier.</summary>
    public required string GenerationId { get; init; }

    /// <summary>Recognition id that produced the candidate transcript.</summary>
    public required string RecognitionId { get; init; }

    /// <summary>Utterance id that produced the candidate transcript.</summary>
    public required string UtteranceId { get; init; }

    /// <summary>Transcript revision used to start speculative work.</summary>
    public required string TranscriptRevisionId { get; init; }

    /// <summary>Stable transcript text used for generation.</summary>
    public required string TranscriptText { get; init; }

    /// <summary>Fingerprint of chat context used to start generation.</summary>
    public string? ChatContextFingerprint { get; init; }

    /// <summary>Fingerprint of tool set used to start generation.</summary>
    public string? ToolSetFingerprint { get; init; }

    /// <summary>Confidence assigned to this speculative candidate.</summary>
    public float Confidence { get; init; }

    /// <summary>When the candidate was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
