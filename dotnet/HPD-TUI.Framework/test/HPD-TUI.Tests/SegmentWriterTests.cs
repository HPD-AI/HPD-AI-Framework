using HPD.TUI.Core;

namespace HPD.TUI.Tests;

public sealed class SegmentWriterTests
{
    [Fact]
    public void Write_ForwardsSegmentsToSink()
    {
        var sink = new CountingSink();
        var writer = new SegmentWriter(sink);

        Assert.True(writer.Write("Hello", Style.Default));
        Assert.True(writer.WriteLineBreak());

        Assert.Equal(2, writer.Count);
        Assert.Equal(1, sink.TextCount);
        Assert.Equal(1, sink.LineBreakCount);
    }

    private sealed class CountingSink : ISegmentSink
    {
        public int TextCount { get; private set; }

        public int LineBreakCount { get; private set; }

        public int CursorX { get; private set; }

        public int CursorY { get; private set; }

        public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
        {
            TextCount++;
            return true;
        }

        public bool WriteLineBreak()
        {
            LineBreakCount++;
            return true;
        }

        public void MoveTo(int x, int y)
        {
            CursorX = x;
            CursorY = y;
        }

        public void SetTerminalCursor(int x, int y)
        {
        }
    }
}
