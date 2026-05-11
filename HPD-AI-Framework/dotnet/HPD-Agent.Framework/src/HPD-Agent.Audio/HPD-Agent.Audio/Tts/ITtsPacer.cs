// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.Tts;

/// <summary>
/// Converts model text deltas into TTS synthesis text segments.
/// </summary>
public interface ITtsPacer
{
    /// <summary>
    /// Segments model text into TTS requests.
    /// </summary>
    IAsyncEnumerable<TtsTextSegment> SegmentAsync(
        IAsyncEnumerable<string> modelText,
        SpeechOutputState outputState,
        TtsPacingOptions options,
        CancellationToken cancellationToken = default);
}
