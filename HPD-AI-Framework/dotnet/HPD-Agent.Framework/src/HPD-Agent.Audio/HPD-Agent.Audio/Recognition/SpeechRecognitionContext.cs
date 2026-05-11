// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Shared correlation and timing context for speech recognition events.
/// </summary>
public sealed record SpeechRecognitionContext(
    string? RuntimeId,
    string? SessionId,
    string? BranchId,
    string UtteranceId,
    string RecognitionId,
    string? SegmentId,
    string? ProviderRequestId,
    string? Provider,
    string? Model,
    long? SequenceNumber,
    long? TimestampNs,
    DateTimeOffset ObservedAt,
    DateTimeOffset? ProviderEventTime = null);
