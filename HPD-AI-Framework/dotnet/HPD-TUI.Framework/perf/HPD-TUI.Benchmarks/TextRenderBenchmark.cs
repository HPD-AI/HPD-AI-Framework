using BenchmarkDotNet.Attributes;
using HPD.TUI.Components;
using HPD.TUI.Rendering;

namespace HPD.TUI.Benchmarks;

[MemoryDiagnoser]
public class TextRenderBenchmark
{
    private readonly Text _shortLine = new("hello from HPD");
    private readonly Text _longLine = new(new string('x', 8_192));
    private readonly Text _manyLines = new(string.Join('\n', Enumerable.Range(0, 1_000).Select(i => $"line {i:D4} {new string('x', 80)}")));
    private readonly Text _wideUnicode = new(string.Concat(Enumerable.Repeat("測試🙂", 2_000)));

    [Benchmark(Baseline = true)]
    public string ShortLine()
        => Render(_shortLine, width: 80, height: 4);

    [Benchmark]
    public string LongLine()
        => Render(_longLine, width: 80, height: 32);

    [Benchmark]
    public string ManyLines()
        => Render(_manyLines, width: 100, height: 48);

    [Benchmark]
    public string WideUnicode()
        => Render(_wideUnicode, width: 80, height: 48);

    private static string Render(Text text, int width, int height)
        => TuiCapture.RenderToString(text, width, height, trimTrailingBlankLines: false);
}
