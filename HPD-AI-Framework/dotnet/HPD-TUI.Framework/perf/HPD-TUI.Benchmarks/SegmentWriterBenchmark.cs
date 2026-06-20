using BenchmarkDotNet.Attributes;
using HPD.TUI.Core;

namespace HPD.TUI.Benchmarks;

[MemoryDiagnoser]
public class SegmentWriterBenchmark
{
    private readonly CountingSink _sink = new();
    private readonly string _largeText = new('x', 8_192);

    [Benchmark(Baseline = true)]
    public int ManySmallWrites()
    {
        _sink.Reset();
        var writer = new SegmentWriter(_sink);
        for (var i = 0; i < 1_000; i++)
        {
            writer.Write("x".AsSpan(), Style.Default);
        }

        return writer.Count;
    }

    [Benchmark]
    public int FewerLargeWrites()
    {
        _sink.Reset();
        var writer = new SegmentWriter(_sink);
        for (var i = 0; i < 32; i++)
        {
            writer.Write(_largeText.AsSpan(), Style.Default);
        }

        return writer.Count;
    }

    [Benchmark]
    public int StyledSegmentWrites()
    {
        _sink.Reset();
        var writer = new SegmentWriter(_sink);
        var style = new Style(Color.Cyan, Color.Default, TextAttributes.Bold);
        for (var i = 0; i < 1_000; i++)
        {
            writer.Write("styled".AsSpan(), style);
        }

        return writer.Count;
    }

    [Benchmark]
    public int RepeatedWrites()
    {
        _sink.Reset();
        var writer = new SegmentWriter(_sink);
        writer.WriteRepeated('-', 16_384, Style.Default);
        return writer.Count;
    }

    private sealed class CountingSink : ISegmentSink
    {
        public int CursorX { get; private set; }
        public int CursorY { get; private set; }

        public bool Write(scoped ReadOnlySpan<char> text, Style style)
        {
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

        public void Reset()
        {
            CursorX = 0;
            CursorY = 0;
        }
    }
}
