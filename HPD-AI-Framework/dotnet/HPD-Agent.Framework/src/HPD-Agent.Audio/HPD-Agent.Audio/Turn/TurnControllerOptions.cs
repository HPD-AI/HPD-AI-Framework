// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Eot;

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Policy options for user turn control and endpointing.
/// </summary>
public sealed record TurnControllerOptions
{
    /// <summary>Endpointing mode. Defaults to conservative hybrid behavior.</summary>
    public EndpointingMode Mode { get; init; } = EndpointingMode.Hybrid;

    /// <summary>Minimum endpointing delay after speech appears done.</summary>
    public TimeSpan MinEndpointingDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Maximum endpointing delay for low-confidence completion.</summary>
    public TimeSpan MaxEndpointingDelay { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>Probability at or above which the minimum delay is used.</summary>
    public float HighConfidenceThreshold { get; init; } = 0.8f;

    /// <summary>Optional detector used to score final transcript completion.</summary>
    public IEotDetector? EotDetector { get; init; }
}
