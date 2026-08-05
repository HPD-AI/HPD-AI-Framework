// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Audio.Output;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal sealed class ElevenLabsTtsRuntimeSettings
{
    public string? BaseUrl { get; init; }
    public string? WebSocketBaseUrl { get; init; }
    public string? DefaultModelId { get; init; }
    public string? DefaultVoiceId { get; init; }
    public string? OutputFormat { get; init; }
    public double? Stability { get; init; }
    public double? SimilarityBoost { get; init; }
    public double? Style { get; init; }
    public bool? UseSpeakerBoost { get; init; }
    public double? Speed { get; init; }
    public string? ApplyTextNormalization { get; init; }
    public bool EnablePushTextStreaming { get; init; }
    public PushTextInputAggregationMode PushTextAggregationMode { get; init; } = PushTextInputAggregationMode.Sentence;
    public bool? AutoMode { get; init; }
    public bool? SyncAlignment { get; init; }
    public int? InactivityTimeout { get; init; }
}

internal sealed class ElevenLabsSttRuntimeSettings
{
    public string? BaseUrl { get; init; }
    public string? WebSocketBaseUrl { get; init; }
    public string? DefaultModelId { get; init; }
    public string? RealtimeModelId { get; init; }
    public string? LanguageCode { get; init; }
    public bool? Diarize { get; init; }
    public bool? TagAudioEvents { get; init; }
    public string? TimestampsGranularity { get; init; }
    public string? AudioFormat { get; init; }
    public string? CommitStrategy { get; init; }
    public bool? IncludeTimestamps { get; init; }
    public bool? IncludeLanguageDetection { get; init; }
    public string[]? Keyterms { get; init; }
    public bool? NoVerbatim { get; init; }
    public double? VadSilenceThresholdSeconds { get; init; }
    public double? VadThreshold { get; init; }
    public int? MinSpeechDurationMilliseconds { get; init; }
    public int? MinSilenceDurationMilliseconds { get; init; }
    public bool? EnableLogging { get; init; }
    public int? StreamingChunkSizeBytes { get; init; }
}
