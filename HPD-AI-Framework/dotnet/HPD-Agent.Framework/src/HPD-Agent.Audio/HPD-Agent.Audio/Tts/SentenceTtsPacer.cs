// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Text;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.Tts;

/// <summary>
/// Conservative TTS pacer that flushes the first complete sentence quickly and final remainder at stream end.
/// </summary>
public sealed class SentenceTtsPacer : ITtsPacer
{
    /// <inheritdoc />
    public async IAsyncEnumerable<TtsTextSegment> SegmentAsync(
        IAsyncEnumerable<string> modelText,
        SpeechOutputState outputState,
        TtsPacingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelText);
        ArgumentNullException.ThrowIfNull(outputState);
        ArgumentNullException.ThrowIfNull(options);

        var buffer = new StringBuilder();

        await foreach (var delta in modelText.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(delta) || !options.EnableQuickAnswer)
                continue;

            buffer.Append(delta);

            if (!IsSentenceBoundary(buffer.ToString()))
                continue;

            var text = ApplyFilter(buffer.ToString(), options);
            buffer.Clear();

            if (!string.IsNullOrWhiteSpace(text))
                yield return new TtsTextSegment(text, IsFinal: false, Reason: "sentence_boundary");
        }

        if (buffer.Length > 0)
        {
            var text = ApplyFilter(buffer.ToString(), options);
            if (!string.IsNullOrWhiteSpace(text))
                yield return new TtsTextSegment(text, IsFinal: true, Reason: "model_complete");
        }
    }

    private static string ApplyFilter(string text, TtsPacingOptions options) =>
        options.TextFilter?.Invoke(text) ?? text;

    private static bool IsSentenceBoundary(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?');
    }
}
