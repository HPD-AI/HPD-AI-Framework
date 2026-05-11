// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Observable endpointing decision emitted before a turn is committed.
/// </summary>
public sealed record EndpointingDecision
{
    /// <summary>Whether the turn can commit immediately.</summary>
    public bool ShouldCommitNow { get; init; }

    /// <summary>Delay before commit when not immediate.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>End-of-turn probability used by the decision, if available.</summary>
    public float? EotProbability { get; init; }

    /// <summary>Stable reason string for tests, metrics, and replay.</summary>
    public string Reason { get; init; } = "";
}
