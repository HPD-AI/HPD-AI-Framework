// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Configuration for end-of-turn detection.
/// </summary>
public class EotConfig
{
    /// <summary>
    /// EOT provider key. Default: "heuristic-eot".
    /// </summary>
    public string Provider { get; set; } = "heuristic-eot";

    /// <summary>Silence detection strategy. Default: FastPath.</summary>
    public EotDetectionStrategy SilenceStrategy { get; set; } = EotDetectionStrategy.FastPath;

    /// <summary>Detector strategy. Default: OnAmbiguous.</summary>
    public EotDetectionStrategy DetectorStrategy { get; set; } = EotDetectionStrategy.OnAmbiguous;

    /// <summary>Silence threshold for fast-path completion. Default: 1.5s.</summary>
    public float SilenceFastPathThreshold { get; set; } = 1.5f;

    /// <summary>Minimum endpointing delay when completion confidence is high. Default: 0.3s.</summary>
    public float MinEndpointingDelay { get; set; } = 0.3f;

    /// <summary>Maximum endpointing delay when completion confidence is low. Default: 1.5s.</summary>
    public float MaxEndpointingDelay { get; set; } = 1.5f;

    /// <summary>Multiplier for silence-based probability boost. Default: 0.7.</summary>
    public float SilenceBoostMultiplier { get; set; } = 0.7f;

    /// <summary>Whether to combine detector and silence probabilities. Default: true.</summary>
    public bool UseCombinedProbability { get; set; } = true;

    /// <summary>Custom trailing words that indicate incomplete thoughts.</summary>
    public HashSet<string>? CustomTrailingWords { get; set; }

    /// <summary>Probability returned for punctuation that is weakened by an incomplete trailing word.</summary>
    public float TrailingWordPenalty { get; set; } = 0.6f;

    /// <summary>Provider-specific configuration as JSON string.</summary>
    public string? ProviderOptionsJson { get; set; }

    /// <summary>Validates EOT configuration.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider))
            throw new ArgumentException("EOT Provider is required", nameof(Provider));

        if (SilenceFastPathThreshold < 0)
            throw new ArgumentException("SilenceFastPathThreshold must be non-negative", nameof(SilenceFastPathThreshold));

        if (MinEndpointingDelay < 0)
            throw new ArgumentException("MinEndpointingDelay must be non-negative", nameof(MinEndpointingDelay));

        if (MaxEndpointingDelay < 0)
            throw new ArgumentException("MaxEndpointingDelay must be non-negative", nameof(MaxEndpointingDelay));

        if (SilenceBoostMultiplier is < 0 or > 1.0f)
            throw new ArgumentException("SilenceBoostMultiplier must be between 0 and 1.0", nameof(SilenceBoostMultiplier));

        if (TrailingWordPenalty is < 0 or > 1.0f)
            throw new ArgumentException("TrailingWordPenalty must be between 0 and 1.0", nameof(TrailingWordPenalty));
    }
}
