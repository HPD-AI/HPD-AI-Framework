// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Word-level recognition detail when a provider can report it.
/// </summary>
public sealed record SpeechRecognitionWord(
    string Text,
    TimeSpan? StartTime = null,
    TimeSpan? EndTime = null,
    float? Confidence = null,
    string? SpeakerId = null);

/// <summary>
/// Transcript payload shared by interim, preflight, and final recognition events.
/// </summary>
public sealed record SpeechRecognitionTranscript(
    string Text,
    float? Confidence = null,
    string? Language = null,
    TimeSpan? StartTime = null,
    TimeSpan? EndTime = null,
    IReadOnlyList<SpeechRecognitionWord>? Words = null,
    string? SpeakerId = null,
    bool? IsPrimarySpeaker = null,
    string? TranscriptRevisionId = null);
