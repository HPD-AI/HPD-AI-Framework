using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class SentenceTtsPacerTests
{
    [Fact]
    public void PushText_EmitsCompleteSentenceEarly()
    {
        var pacer = new SentenceTtsPacer();
        var context = CreateContext(new TextToSpeechPacingOptions
        {
            First = new TextToSpeechFirstSegmentOptions
            {
                MinCharacters = 1
            }
        });

        Assert.Empty(pacer.PushText("Hello", context));
        var segments = pacer.PushText(" there. Next", context);

        var segment = Assert.Single(segments);
        Assert.Equal(new OutputSegmentId("output-test:tts-0000"), segment.SegmentId);
        Assert.Equal("Hello there.", segment.Text);
        Assert.Equal(0, segment.SegmentIndex);
        Assert.False(segment.IsFinalSegment);
        Assert.Equal(TextToSpeechSegmentKind.Sentence, segment.Kind);
        Assert.Equal(0, segment.SourceTextStart);
        Assert.Equal("Hello there.".Length, segment.SourceTextLength);
    }

    [Fact]
    public void Flush_EmitsFinalRemainder()
    {
        var pacer = new SentenceTtsPacer();
        var context = CreateContext(new TextToSpeechPacingOptions
        {
            First = new TextToSpeechFirstSegmentOptions
            {
                MinCharacters = 1
            }
        });

        Assert.Empty(pacer.PushText("The answer is still forming", context));
        var segments = pacer.Flush(context);

        var segment = Assert.Single(segments);
        Assert.Equal("The answer is still forming", segment.Text);
        Assert.Equal(0, segment.SegmentIndex);
        Assert.True(segment.IsFinalSegment);
        Assert.Equal(TextToSpeechSegmentKind.Remainder, segment.Kind);
        Assert.Equal(0, segment.SourceTextStart);
        Assert.Equal("The answer is still forming".Length, segment.SourceTextLength);
        Assert.Empty(pacer.Flush(context));
    }

    [Fact]
    public void PushText_UsesMaxBufferedCharsFallbackWithoutEmptySegments()
    {
        var pacer = new SentenceTtsPacer();
        var context = CreateContext(new TextToSpeechPacingOptions
        {
            First = new TextToSpeechFirstSegmentOptions
            {
                MaxCharacters = 12
            },
            Continuation = new TextToSpeechContinuationOptions
            {
                MaxCharacters = 12
            }
        });

        Assert.Empty(pacer.PushText("   ", context));
        var segments = pacer.PushText("alpha beta gamma", context);

        var segment = Assert.Single(segments);
        Assert.Equal("alpha", segment.Text);
        Assert.Equal(0, segment.SegmentIndex);
        Assert.False(segment.IsFinalSegment);
        Assert.Equal(TextToSpeechSegmentKind.TokenBatch, segment.Kind);
        Assert.Equal(3, segment.SourceTextStart);
        Assert.Equal("alpha".Length, segment.SourceTextLength);
    }

    [Fact]
    public void PushText_PreservesStableSequenceNumbersAndRanges()
    {
        var pacer = new SentenceTtsPacer();
        var context = CreateContext(new TextToSpeechPacingOptions
        {
            First = new TextToSpeechFirstSegmentOptions
            {
                MinCharacters = 1
            }
        });

        Assert.Empty(pacer.PushText("One. ", context));
        var first = Assert.Single(pacer.PushText("Two? ", context));
        var second = Assert.Single(pacer.PushText("tail", context));
        var third = Assert.Single(pacer.Flush(context));

        Assert.Equal(new OutputSegmentId("output-test:tts-0000"), first.SegmentId);
        Assert.Equal(new OutputSegmentId("output-test:tts-0001"), second.SegmentId);
        Assert.Equal(new OutputSegmentId("output-test:tts-0002"), third.SegmentId);
        Assert.Equal(0, first.SegmentIndex);
        Assert.Equal(1, second.SegmentIndex);
        Assert.Equal(2, third.SegmentIndex);
        Assert.Equal(0, first.SourceTextStart);
        Assert.Equal(5, second.SourceTextStart);
        Assert.Equal(10, third.SourceTextStart);
        Assert.Equal("One.", first.Text);
        Assert.Equal("Two?", second.Text);
        Assert.Equal("tail", third.Text);
    }

    [Theory]
    [InlineData("Dr. Smith is here. Next", "Dr. Smith is here.")]
    [InlineData("$29.99 is the price. Next", "$29.99 is the price.")]
    [InlineData("https://example.com/path. Next", "https://example.com/path. Next")]
    public void PushText_ProtectsFalseSentenceBoundaries(string text, string expectedFirstSegment)
    {
        var pacer = new SentenceTtsPacer();
        var context = CreateContext(new TextToSpeechPacingOptions
        {
            First = new TextToSpeechFirstSegmentOptions
            {
                MinCharacters = 1
            }
        });

        var pushed = pacer.PushText(text, context);
        var segments = pushed.Count == 0 ? pacer.Flush(context) : pushed;

        var segment = Assert.Single(segments);
        Assert.Equal(expectedFirstSegment, segment.Text);
    }

    [Fact]
    public void PushText_ManualModeOnlyEmitsOnFlush()
    {
        var pacer = new SentenceTtsPacer();
        var context = CreateContext(new TextToSpeechPacingOptions
        {
            Mode = TextToSpeechPacingMode.Manual
        });

        Assert.Empty(pacer.PushText("One. Two.", context));

        var segment = Assert.Single(pacer.Flush(context));
        Assert.Equal("One. Two.", segment.Text);
    }

    private static TextToSpeechPacingContext CreateContext(TextToSpeechPacingOptions? options = null)
    {
        return new TextToSpeechPacingContext
        {
            OutputFlowId = new OutputFlowId("output-test"),
            ResponseId = new ResponseId("response-test"),
            Options = options ?? new TextToSpeechPacingOptions()
        };
    }
}
