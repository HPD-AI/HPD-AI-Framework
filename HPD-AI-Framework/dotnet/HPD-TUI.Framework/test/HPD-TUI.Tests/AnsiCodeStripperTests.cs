using HPD.TUI.Utilities;

namespace HPD.TUI.Tests;

public sealed class AnsiCodeStripperTests
{
    [Fact]
    public void VisibleLength_IgnoresSgrSequences()
    {
        var length = AnsiCodeStripper.VisibleLength("\x1b[1mHello\x1b[0m");

        Assert.Equal(5, length);
    }

    [Fact]
    public void VisibleLength_IgnoresOscHyperlinks()
    {
        var length = AnsiCodeStripper.VisibleLength("\x1b]8;;https://example.com\x07Link\x1b]8;;\x07");

        Assert.Equal(4, length);
    }

    [Fact]
    public void Strip_RemovesAnsiSequences()
    {
        Span<char> buffer = stackalloc char[16];

        var written = AnsiCodeStripper.Strip("\x1b[38;2;255;0;0mRed\x1b[0m", buffer);

        Assert.Equal(3, written);
        Assert.True(buffer[..written].SequenceEqual("Red"));
    }

    [Fact]
    public void SkipEscapeSequence_DoesNotReadPastEndForTruncatedCsi()
    {
        var next = AnsiCodeStripper.SkipEscapeSequence("\x1b[123;", 0);

        Assert.Equal(6, next);
    }
}
