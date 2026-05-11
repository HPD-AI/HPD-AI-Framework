// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Runtime options for a speech recognition session.
/// </summary>
public sealed record SpeechRecognitionOptions
{
    /// <summary>Provider name to record in recognition context.</summary>
    public string? Provider { get; init; }

    /// <summary>Model name to use for recognition and record in context.</summary>
    public string? Model { get; init; }

    /// <summary>Expected speech language.</summary>
    public string? Language { get; init; }

    /// <summary>Runtime id for correlation, when recognition is runtime-scoped.</summary>
    public string? RuntimeId { get; init; }

    /// <summary>Session id for correlation.</summary>
    public string? SessionId { get; init; }

    /// <summary>Branch id for correlation.</summary>
    public string? BranchId { get; init; }

    /// <summary>Optional caller-provided recognition id.</summary>
    public string? RecognitionId { get; init; }

    /// <summary>Optional caller-provided utterance id.</summary>
    public string? UtteranceId { get; init; }

    /// <summary>Input audio MIME type.</summary>
    public string? AudioMimeType { get; init; }

    /// <summary>Input audio sample rate in Hz.</summary>
    public int? SampleRate { get; init; }

    /// <summary>Input audio channel count.</summary>
    public int? Channels { get; init; }
}
