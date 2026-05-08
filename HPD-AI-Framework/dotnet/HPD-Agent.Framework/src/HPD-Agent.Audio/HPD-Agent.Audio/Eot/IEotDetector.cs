// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Detects whether transcribed text has reached an end-of-turn boundary.
/// </summary>
public interface IEotDetector
{
    /// <summary>
    /// Gets the probability that the given text is end-of-turn.
    /// </summary>
    float GetEndOfTurnProbability(string text);

    /// <summary>
    /// Resets internal state for a new utterance.
    /// </summary>
    void Reset();
}
