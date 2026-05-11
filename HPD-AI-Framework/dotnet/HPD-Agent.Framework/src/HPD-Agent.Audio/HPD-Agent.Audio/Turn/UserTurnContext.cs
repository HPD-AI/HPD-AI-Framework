// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Shared correlation and timing context for user turn events.
/// </summary>
public sealed record UserTurnContext(
    string? RuntimeId,
    string? SessionId,
    string? BranchId,
    string TurnId,
    string UtteranceId,
    string? RecognitionId,
    string? TranscriptRevisionId,
    long? SequenceNumber,
    long? TimestampNs,
    DateTimeOffset ObservedAt);
