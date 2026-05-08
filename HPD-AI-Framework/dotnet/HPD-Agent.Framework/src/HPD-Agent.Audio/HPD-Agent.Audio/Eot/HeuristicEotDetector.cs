// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Simple punctuation-based end-of-turn detector.
/// </summary>
public class HeuristicEotDetector : IEotDetector
{
    private static readonly HashSet<string> DefaultTrailingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "so", "because", "when", "if", "that", "which", "the", "a"
    };

    private readonly HashSet<string> _trailingWords;
    private readonly float _trailingWordPenalty;

    /// <summary>Creates a heuristic EOT detector.</summary>
    public HeuristicEotDetector(EotConfig? config = null)
    {
        _trailingWords = config?.CustomTrailingWords is { Count: > 0 }
            ? new HashSet<string>(config.CustomTrailingWords, StringComparer.OrdinalIgnoreCase)
            : DefaultTrailingWords;
        _trailingWordPenalty = config?.TrailingWordPenalty ?? 0.6f;
    }

    /// <inheritdoc />
    public float GetEndOfTurnProbability(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.0f;

        var trimmed = text.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?'))
        {
            if (words.Length >= 1)
            {
                var lastWord = words[^1].TrimEnd('.', '!', '?');
                if (_trailingWords.Contains(lastWord))
                    return _trailingWordPenalty;
            }

            return 0.9f;
        }

        if (trimmed.EndsWith(',') || trimmed.EndsWith(';') || trimmed.EndsWith(':'))
            return 0.3f;

        if (trimmed.EndsWith("...") || trimmed.EndsWith("…"))
            return 0.2f;

        if (trimmed.EndsWith('"') || trimmed.EndsWith('\'') || trimmed.EndsWith('"'))
            return 0.7f;

        if (trimmed.EndsWith(')') || trimmed.EndsWith(']'))
            return 0.6f;

        return 0.1f;
    }

    /// <inheritdoc />
    public void Reset()
    {
    }
}
