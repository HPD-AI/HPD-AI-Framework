// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Tts;

/// <summary>
/// Policy knobs for TTS text pacing.
/// </summary>
public sealed record TtsPacingOptions
{
    /// <summary>Whether sentence-boundary quick answer is enabled.</summary>
    public bool EnableQuickAnswer { get; init; } = true;

    /// <summary>Optional text filter applied before a segment is emitted.</summary>
    public Func<string, string>? TextFilter { get; init; }
}
