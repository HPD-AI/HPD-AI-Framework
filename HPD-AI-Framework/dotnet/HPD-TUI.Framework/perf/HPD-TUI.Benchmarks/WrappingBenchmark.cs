using BenchmarkDotNet.Attributes;
using HPD.TUI.Components;
using HPD.TUI.Rendering;

namespace HPD.TUI.Benchmarks;

[MemoryDiagnoser]
public class WrappingBenchmark
{
    private readonly Text _longAscii = new(new string('a', 16_384));
    private readonly Text _tabs = new(string.Concat(Enumerable.Repeat("alpha\tbeta\tgamma\t", 1_000)));
    private readonly Text _wideUnicode = new(string.Concat(Enumerable.Repeat("界🙂測試", 2_000)));

    [Benchmark(Baseline = true)]
    public string LongAsciiWide()
        => Render(_longAscii, width: 120);

    [Benchmark]
    public string LongAsciiNarrow()
        => Render(_longAscii, width: 24);

    [Benchmark]
    public string Tabs()
        => Render(_tabs, width: 80);

    [Benchmark]
    public string WideUnicode()
        => Render(_wideUnicode, width: 80);

    private static string Render(Text text, int width)
        => TuiCapture.RenderToString(text, width, height: 64, trimTrailingBlankLines: false);
}
