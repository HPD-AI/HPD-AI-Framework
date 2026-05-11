// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Tts;
using Xunit;

namespace HPD.Agent.Audio.Tests.Tts;

public sealed class SentenceTtsPacerTests
{
    [Fact]
    public async Task SegmentAsync_EmitsOnSentenceBoundaryAndFinalRemainder()
    {
        var pacer = new SentenceTtsPacer();

        var segments = await CollectAsync(pacer.SegmentAsync(
            ToAsync(["Hello", " world.", " Next"]),
            new SpeechOutputState(),
            new TtsPacingOptions(),
            CancellationToken.None));

        Assert.Collection(
            segments,
            segment =>
            {
                Assert.Equal("Hello world.", segment.Text);
                Assert.False(segment.IsFinal);
                Assert.Equal("sentence_boundary", segment.Reason);
            },
            segment =>
            {
                Assert.Equal(" Next", segment.Text);
                Assert.True(segment.IsFinal);
                Assert.Equal("model_complete", segment.Reason);
            });
    }

    [Fact]
    public async Task SegmentAsync_AppliesTextFilter()
    {
        var pacer = new SentenceTtsPacer();

        var segments = await CollectAsync(pacer.SegmentAsync(
            ToAsync(["Hello **world**."]),
            new SpeechOutputState(),
            new TtsPacingOptions { TextFilter = text => text.Replace("**", "", StringComparison.Ordinal) },
            CancellationToken.None));

        var segment = Assert.Single(segments);
        Assert.Equal("Hello world.", segment.Text);
    }

    [Fact]
    public async Task SegmentAsync_WhenQuickAnswerDisabled_DoesNotEmitSegments()
    {
        var pacer = new SentenceTtsPacer();

        var segments = await CollectAsync(pacer.SegmentAsync(
            ToAsync(["Hello world."]),
            new SpeechOutputState(),
            new TtsPacingOptions { EnableQuickAnswer = false },
            CancellationToken.None));

        Assert.Empty(segments);
    }

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }
    }

    private static async Task<List<TtsTextSegment>> CollectAsync(IAsyncEnumerable<TtsTextSegment> segments)
    {
        var results = new List<TtsTextSegment>();
        await foreach (var segment in segments)
            results.Add(segment);
        return results;
    }
}
