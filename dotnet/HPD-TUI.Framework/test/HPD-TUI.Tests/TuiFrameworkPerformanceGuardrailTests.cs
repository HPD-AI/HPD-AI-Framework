using System.Diagnostics;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Utilities;

namespace HPD.TUI.Tests;

public sealed class TuiFrameworkPerformanceGuardrailTests
{
    [Fact]
    public void Text_Render_LongLine_IsBoundedByCaptureHeight()
    {
        var text = new Text(new string('x', 50_000));
        var stopwatch = Stopwatch.StartNew();

        var lines = TuiCapture.RenderToLines(text, width: 40, height: 20, trimTrailingBlankLines: false);

        stopwatch.Stop();
        Assert.Equal(20, lines.Length);
        Assert.All(lines, line => Assert.True(line.Length <= 40));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Long text render took {stopwatch.Elapsed.TotalMilliseconds:0.###}ms.");
    }

    [Fact]
    public void Text_Render_ManyLines_DoesNotAllocateExcessively()
    {
        var text = new Text(string.Join('\n', Enumerable.Range(0, 5_000).Select(static i => $"line {i:D4}")));
        var before = GC.GetAllocatedBytesForCurrentThread();

        var lines = TuiCapture.RenderToLines(text, width: 80, height: 24, trimTrailingBlankLines: false);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(24, lines.Length);
        Assert.True(allocated < 2_000_000, $"Many-line render allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Markdown_Render_RepeatedSameWidth_IsStableAndBounded()
    {
        var markdown = new HPD.TUI.Components.Markdown(string.Join(
            "\n",
            Enumerable.Range(0, 200).Select(static i => $"- item {i:D4} with enough words to wrap at narrow widths")));

        var expected = TuiCapture.RenderToString(markdown, width: 48, height: 80, trimTrailingBlankLines: true);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 20; i++)
        {
            var rendered = TuiCapture.RenderToString(markdown, width: 48, height: 80, trimTrailingBlankLines: true);
            Assert.Equal(expected, rendered);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Repeated markdown render took {stopwatch.Elapsed.TotalMilliseconds:0.###}ms.");
    }

    [Fact]
    public void Markdown_Render_WidthChange_InvalidatesOnlyWidthDependentCache()
    {
        var markdown = new HPD.TUI.Components.Markdown(string.Join(
            "\n",
            Enumerable.Range(0, 200).Select(static i => $"- item {i:D4} with enough words to wrap at narrow widths")));
        TuiCapture.RenderToString(markdown, width: 80, height: 80, trimTrailingBlankLines: true);

        var narrowElapsed = Measure(() => TuiCapture.RenderToString(
            markdown,
            width: 40,
            height: 80,
            trimTrailingBlankLines: true));
        var wideElapsed = Measure(() => TuiCapture.RenderToString(
            markdown,
            width: 100,
            height: 80,
            trimTrailingBlankLines: true));

        Assert.True(
            narrowElapsed + wideElapsed < TimeSpan.FromSeconds(5),
            $"Markdown width changes took {(narrowElapsed + wideElapsed).TotalMilliseconds:0.###}ms.");
    }

    [Fact]
    public void UnicodeWidth_LongAscii_IsLinearShape()
    {
        var small = new string('a', 20_000);
        var large = new string('a', 40_000);
        UnicodeWidth.GetWidth(small);
        UnicodeWidth.GetWidth(large);

        var smallElapsed = Measure(() => UnicodeWidth.GetWidth(small));
        var largeElapsed = Measure(() => UnicodeWidth.GetWidth(large));

        Assert.True(
            largeElapsed.TotalMilliseconds <= (smallElapsed.TotalMilliseconds * 6) + 25,
            $"Width calculation grew unexpectedly: small={smallElapsed.TotalMilliseconds:0.###}ms large={largeElapsed.TotalMilliseconds:0.###}ms.");
    }

    [Fact]
    public void UnicodeWidth_WideUnicode_IsLinearShape()
    {
        var small = string.Concat(Enumerable.Repeat("界", 10_000));
        var large = string.Concat(Enumerable.Repeat("界", 20_000));
        UnicodeWidth.GetWidth(small);
        UnicodeWidth.GetWidth(large);

        var smallElapsed = Measure(() => UnicodeWidth.GetWidth(small));
        var largeElapsed = Measure(() => UnicodeWidth.GetWidth(large));

        Assert.True(
            largeElapsed.TotalMilliseconds <= (smallElapsed.TotalMilliseconds * 6) + 25,
            $"Wide width calculation grew unexpectedly: small={smallElapsed.TotalMilliseconds:0.###}ms large={largeElapsed.TotalMilliseconds:0.###}ms.");
    }

    [Fact]
    public void SegmentWriter_WriteRepeated_StaysChunkBounded()
    {
        var sink = new CountingSink();
        var writer = new SegmentWriter(sink);

        Assert.True(writer.WriteRepeated('x', 10_000, Style.Default));

        Assert.Equal(10_000, sink.CharactersWritten);
        Assert.True(writer.Count <= 157, $"Expected 10,000 repeated chars to be chunked, but wrote {writer.Count} segments.");
    }

    [Fact]
    public void Wrapping_LongLine_IsLinearShape()
    {
        var small = new Text(new string('x', 20_000));
        var large = new Text(new string('x', 40_000));
        TuiCapture.RenderToString(small, width: 40, height: 80, trimTrailingBlankLines: false);
        TuiCapture.RenderToString(large, width: 40, height: 80, trimTrailingBlankLines: false);

        var smallElapsed = Measure(() => TuiCapture.RenderToString(
            small,
            width: 40,
            height: 80,
            trimTrailingBlankLines: false));
        var largeElapsed = Measure(() => TuiCapture.RenderToString(
            large,
            width: 40,
            height: 80,
            trimTrailingBlankLines: false));

        Assert.True(
            largeElapsed.TotalMilliseconds <= (smallElapsed.TotalMilliseconds * 6) + 25,
            $"Wrapping grew unexpectedly: small={smallElapsed.TotalMilliseconds:0.###}ms large={largeElapsed.TotalMilliseconds:0.###}ms.");
    }

    private static TimeSpan Measure(Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private sealed class CountingSink : ISegmentSink
    {
        public int CharactersWritten { get; private set; }

        public int CursorX { get; private set; }

        public int CursorY { get; private set; }

        public bool Write(scoped ReadOnlySpan<char> text, Style style)
        {
            CharactersWritten += text.Length;
            CursorX += text.Length;
            return true;
        }

        public bool WriteLineBreak()
        {
            CursorX = 0;
            CursorY++;
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
