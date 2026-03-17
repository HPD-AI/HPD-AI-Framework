// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio;

/// <summary>
/// Simple punctuation-based turn detector. No ML required.
/// Uses common sentence-ending patterns to estimate turn completion probability.
/// Enhanced with trailing word detection and word count heuristics.
/// </summary>
public class HeuristicTurnDetector : ITurnDetector
{
    // Trailing words that indicate incomplete thoughts
    private static readonly HashSet<string> TrailingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "so", "because", "when", "if", "that", "which", "the", "a"
    };

    /// <summary>
    /// Gets the probability that the given text represents a complete utterance
    /// based on punctuation patterns, trailing words, and length.
    /// </summary>
    /// <param name="text">The transcribed text to analyze.</param>
    /// <returns>Probability between 0.0 and 1.0 that the turn is complete.</returns>
    public float GetCompletionProbability(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.0f;

        var trimmed = text.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Strong sentence endings
        if (trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?'))
        {
            // Check for trailing incomplete markers (e.g., "I was thinking and.")
            if (words.Length >= 1)
            {
                var lastWord = words[^1].TrimEnd('.', '!', '?');
                if (TrailingWords.Contains(lastWord))
                    return 0.6f; // Likely incomplete despite punctuation
            }

            return 0.9f;
        }

        // Weak endings - likely mid-sentence
        if (trimmed.EndsWith(',') || trimmed.EndsWith(';') || trimmed.EndsWith(':'))
            return 0.3f;

        // Trailing off - uncertain
        if (trimmed.EndsWith("...") || trimmed.EndsWith("…"))
            return 0.2f;

        // Quotation marks - might be complete
        if (trimmed.EndsWith('"') || trimmed.EndsWith('\'') || trimmed.EndsWith('"'))
            return 0.7f;

        // Parenthesis - might be complete
        if (trimmed.EndsWith(')') || trimmed.EndsWith(']'))
            return 0.6f;

        // No punctuation - likely incomplete
        return 0.1f;
    }

    /// <summary>
    /// Resets internal state (no-op for heuristic detector).
    /// </summary>
    public void Reset()
    {
        // No state to reset for heuristic detection
    }
}
