// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio;

/// <summary>
/// Detects when the user has finished speaking (turn completion).
/// Two implementations: heuristic (built-in) and ML (ONNX provider).
/// </summary>
public interface ITurnDetector
{
    /// <summary>
    /// Gets the probability that the given text represents a complete utterance.
    /// </summary>
    /// <param name="text">The transcribed text to analyze.</param>
    /// <returns>Probability between 0.0 and 1.0 that the turn is complete.</returns>
    float GetCompletionProbability(string text);

    /// <summary>
    /// Resets internal state for a new conversation turn.
    /// </summary>
    void Reset();
}
