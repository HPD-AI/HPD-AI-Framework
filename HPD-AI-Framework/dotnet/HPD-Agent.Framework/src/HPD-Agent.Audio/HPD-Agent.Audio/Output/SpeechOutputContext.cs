// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Output;

/// <summary>
/// Shared correlation and timing context for normalized HPD speech output events.
/// </summary>
public sealed record SpeechOutputContext(
    string? RuntimeId,
    string? SessionId,
    string? BranchId,
    string SpeechId,
    string StreamId,
    string? OutputId,
    string? Provider,
    string? Model,
    string? Voice,
    long? SequenceNumber,
    long? TimestampNs,
    DateTimeOffset ObservedAt);
