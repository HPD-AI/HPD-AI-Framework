// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.AudioProviders.Silero;

/// <summary>
/// Silero-specific VAD configuration.
/// Passed via VadConfig.ProviderOptionsJson as JSON.
/// </summary>
public class SileroVadConfig
{
    /// <summary>
    /// Force CPU execution provider for ONNX inference.
    /// Set to false to allow GPU (CUDA/DirectML) if available.
    /// Default: true (CPU only, consistent with reference implementations).
    /// </summary>
    public bool ForceCpu { get; set; } = true;

    /// <summary>
    /// Preferred sample rate for inference.
    /// Must be 8000 or 16000. Default: 16000.
    /// If incoming audio does not match, it will be resampled or an exception thrown.
    /// </summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>
    /// Deactivation threshold — confidence level below which speech is considered ended.
    /// Must be between 0.0 and 1.0. Default: activation_threshold - 0.15 (applied at runtime).
    /// Setting this explicitly overrides the default offset.
    /// </summary>
    public float? DeactivationThreshold { get; set; }

    /// <summary>
    /// How often (seconds) to reset ONNX model internal state.
    /// Prevents memory growth from long sessions. Default: 5.0s.
    /// </summary>
    public float ModelResetIntervalSeconds { get; set; } = 5.0f;
}
