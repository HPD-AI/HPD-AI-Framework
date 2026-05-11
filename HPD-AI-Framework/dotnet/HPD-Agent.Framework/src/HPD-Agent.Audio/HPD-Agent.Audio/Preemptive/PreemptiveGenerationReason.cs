// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Preemptive;

/// <summary>
/// Stable preemptive generation reason constants.
/// </summary>
public static class PreemptiveGenerationReason
{
    /// <summary>Candidate can be reused for the committed turn.</summary>
    public const string Reuse = "reuse";

    /// <summary>No candidate exists for the committed turn.</summary>
    public const string NoCandidate = "no_candidate";

    /// <summary>Candidate transcript does not match the committed transcript.</summary>
    public const string TranscriptMismatch = "transcript_mismatch";

    /// <summary>Candidate context fingerprint does not match the committed context.</summary>
    public const string ContextMismatch = "context_mismatch";

    /// <summary>Candidate tool fingerprint does not match the committed tools.</summary>
    public const string ToolSetMismatch = "tool_set_mismatch";

    /// <summary>Transcript confidence was below the configured threshold.</summary>
    public const string LowConfidence = "low_confidence";

    /// <summary>User continued speaking, invalidating the candidate.</summary>
    public const string UserContinued = "user_continued";
}
