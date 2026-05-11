// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Describes the truthful recognition capabilities of an HPD speech recognizer.
/// </summary>
public sealed record SpeechRecognitionCapabilities
{
    /// <summary>Recognizer can consume live audio frames incrementally.</summary>
    public bool StreamingInput { get; init; }

    /// <summary>Recognizer can emit volatile transcript updates before finalization.</summary>
    public bool InterimResults { get; init; }

    /// <summary>Recognizer can emit stable-enough transcripts for speculative work.</summary>
    public bool PreflightResults { get; init; }

    /// <summary>Recognizer can emit final provider or adapter transcript segments.</summary>
    public bool FinalResults { get; init; } = true;

    /// <summary>Recognizer can provide word-level timestamps.</summary>
    public bool WordTimestamps { get; init; }

    /// <summary>Recognizer can provide segment-level timestamps.</summary>
    public bool SegmentTimestamps { get; init; }

    /// <summary>Recognizer can distinguish speakers.</summary>
    public bool Diarization { get; init; }

    /// <summary>Recognizer can detect spoken language.</summary>
    public bool LanguageDetection { get; init; }

    /// <summary>Recognizer can emit usage or billing events.</summary>
    public bool UsageEvents { get; init; }

    /// <summary>Recognizer can report provider-native speech boundary events.</summary>
    public bool ServerSpeechEvents { get; init; }

    /// <summary>Recognizer supports non-realtime batch recognition.</summary>
    public bool OfflineRecognize { get; init; } = true;
}
