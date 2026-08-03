// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Providers.Audio.ElevenLabs;

/// <summary>ElevenLabs-specific speech-to-text operation options.</summary>
public sealed class ElevenLabsSttOptions : global::HPD.Agent.ISpeechToTextProviderOptions
{
    /// <summary>Gets or sets the model used by realtime transcription.</summary>
    public string? RealtimeModelId { get; set; }
    /// <summary>Gets or sets speaker diarization.</summary>
    public bool? Diarize { get; set; }
    /// <summary>Gets or sets audio-event tagging.</summary>
    public bool? TagAudioEvents { get; set; }
    /// <summary>Gets or sets timestamp granularity.</summary>
    public string? TimestampsGranularity { get; set; }
    /// <summary>Gets or sets realtime input audio format.</summary>
    public string? AudioFormat { get; set; }
    /// <summary>Gets or sets realtime commit strategy.</summary>
    public string? CommitStrategy { get; set; }
    /// <summary>Gets or sets timestamp inclusion.</summary>
    public bool? IncludeTimestamps { get; set; }
    /// <summary>Gets or sets language-detection inclusion.</summary>
    public bool? IncludeLanguageDetection { get; set; }
    /// <summary>Gets or sets recognition key terms.</summary>
    public string[]? Keyterms { get; set; }
    /// <summary>Gets or sets no-verbatim recognition mode.</summary>
    public bool? NoVerbatim { get; set; }
    /// <summary>Gets or sets server VAD silence threshold in seconds.</summary>
    public double? VadSilenceThresholdSeconds { get; set; }
    /// <summary>Gets or sets server VAD activation threshold.</summary>
    public double? VadThreshold { get; set; }
    /// <summary>Gets or sets minimum speech duration in milliseconds.</summary>
    public int? MinSpeechDurationMilliseconds { get; set; }
    /// <summary>Gets or sets minimum silence duration in milliseconds.</summary>
    public int? MinSilenceDurationMilliseconds { get; set; }
    /// <summary>Gets or sets provider-side logging.</summary>
    public bool? EnableLogging { get; set; }
    /// <summary>Gets or sets streaming upload chunk size in bytes.</summary>
    public int? StreamingChunkSizeBytes { get; set; }
}
