using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class TextToSpeechTextSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesCodeBlocksAndPreservesSourceText()
    {
        var sanitizer = new TextToSpeechTextSanitizer();
        var segment = CreateSegment("Here is code: ```csharp\nConsole.WriteLine(1);\n``` Done.");

        var sanitized = sanitizer.Sanitize(segment, new TextToSpeechFilteringOptions());

        Assert.Equal("Here is code: Done.", sanitized.Text);
        Assert.Equal(segment.Text, sanitized.SourceText);
        Assert.Equal(segment.SourceTextStart, sanitized.SourceTextStart);
        Assert.Equal(segment.SourceTextLength, sanitized.SourceTextLength);
        Assert.Equal("default", sanitized.SanitizerPolicyId);
    }

    [Fact]
    public void Sanitize_StripsMarkdownAndSimplifiesLinks()
    {
        var sanitizer = new TextToSpeechTextSanitizer();
        var segment = CreateSegment("Read **the [docs](https://example.test)** now!!!");

        var sanitized = sanitizer.Sanitize(segment, new TextToSpeechFilteringOptions());

        Assert.Equal("Read the docs now!!", sanitized.Text);
        Assert.Equal(segment.Text, sanitized.SourceText);
    }

    [Fact]
    public void Sanitize_CanBeDisabled()
    {
        var sanitizer = new TextToSpeechTextSanitizer();
        var segment = CreateSegment("Read **this**");

        var sanitized = sanitizer.Sanitize(segment, new TextToSpeechFilteringOptions
        {
            Enabled = false
        });

        Assert.Same(segment, sanitized);
    }

    private static TextToSpeechSegment CreateSegment(string text)
    {
        return new TextToSpeechSegment
        {
            SegmentId = new OutputSegmentId("segment-1"),
            Text = text,
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 3,
            SourceTextLength = text.Length
        };
    }
}
