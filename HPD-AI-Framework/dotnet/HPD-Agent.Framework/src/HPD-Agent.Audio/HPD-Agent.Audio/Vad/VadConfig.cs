// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Vad;

/// <summary>
/// Configuration for Voice Activity Detection (VAD).
/// Most settings are pipeline-level and work with any VAD provider.
/// </summary>
public class VadConfig
{
    //
    // PIPELINE-LEVEL VAD SETTINGS
    // (Work with ALL VAD providers: Silero, WebRTC, Deepgram, etc.)
    //

    /// <summary>
    /// Minimum duration of speech to confirm speech start (seconds).
    /// Prevents false positives from short noise bursts.
    /// Default: 0.05 (50ms). Range: 0.01 - 1.0
    /// </summary>
    public float MinSpeechDuration { get; set; } = 0.05f;

    /// <summary>
    /// Minimum duration of silence to confirm speech end (seconds).
    /// Higher = wait longer before detecting pause.
    /// Default: 0.55 (550ms). Range: 0.1 - 3.0
    /// </summary>
    public float MinSilenceDuration { get; set; } = 0.55f;

    /// <summary>
    /// Duration of audio to buffer before confirmed speech (seconds).
    /// Ensures the start of speech is not cut off.
    /// Default: 0.5 (500ms). Range: 0.0 - 2.0
    /// </summary>
    public float PrefixPaddingDuration { get; set; } = 0.5f;

    /// <summary>
    /// Speech probability threshold for activation.
    /// Higher = more conservative (fewer false positives).
    /// Default: 0.5 (50%). Range: 0.0 - 1.0
    /// </summary>
    public float ActivationThreshold { get; set; } = 0.5f;

    //
    // PROVIDER SELECTION
    //

    /// <summary>
    /// VAD provider key (e.g., "silero-vad", "webrtc-vad").
    /// Resolved via VadProviderDiscovery at runtime.
    /// Default: "silero-vad"
    /// </summary>
    public string Provider { get; set; } = "silero-vad";

    /// <summary>
    /// Provider-specific configuration as JSON string.
    /// Most VAD providers have no additional config (settings above are sufficient).
    ///
    /// Examples:
    /// - Silero: {} (no additional config needed)
    /// - WebRTC: {"aggressiveness": 3} (0-3 scale)
    /// </summary>
    public string? ProviderOptionsJson { get; set; }

    /// <summary>
    /// Validates VAD configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider))
            throw new ArgumentException("VAD Provider is required", nameof(Provider));

        if (MinSpeechDuration is < 0.01f or > 1.0f)
            throw new ArgumentException("MinSpeechDuration must be between 0.01 and 1.0", nameof(MinSpeechDuration));

        if (MinSilenceDuration is < 0.1f or > 3.0f)
            throw new ArgumentException("MinSilenceDuration must be between 0.1 and 3.0", nameof(MinSilenceDuration));

        if (PrefixPaddingDuration is < 0.0f or > 2.0f)
            throw new ArgumentException("PrefixPaddingDuration must be between 0.0 and 2.0", nameof(PrefixPaddingDuration));

        if (ActivationThreshold is < 0.0f or > 1.0f)
            throw new ArgumentException("ActivationThreshold must be between 0.0 and 1.0", nameof(ActivationThreshold));
    }
}
