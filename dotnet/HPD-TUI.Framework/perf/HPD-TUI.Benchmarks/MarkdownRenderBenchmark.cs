using BenchmarkDotNet.Attributes;
using HPD.TUI.Rendering;
using MarkdownComponent = HPD.TUI.Content.MarkdownBlock;

namespace HPD.TUI.Benchmarks;

[MemoryDiagnoser]
public class MarkdownRenderBenchmark
{
    private readonly MarkdownComponent _shortMarkdown = MarkdownComponent.Create("# Title\n\nA short paragraph with **bold** and `code`.");
    private readonly MarkdownComponent _longMarkdown = MarkdownComponent.Create(BuildLongMarkdown());
    private readonly MarkdownComponent _codeBlocks = MarkdownComponent.Create(BuildCodeBlocks());
    private readonly MarkdownComponent _lists = MarkdownComponent.Create(BuildLists());
    private int _widthIndex;

    [Benchmark(Baseline = true)]
    public string ShortMarkdown()
        => Render(_shortMarkdown, width: 80, height: 16);

    [Benchmark]
    public string LongMarkdown()
        => Render(_longMarkdown, width: 100, height: 64);

    [Benchmark]
    public string CodeBlocks()
        => Render(_codeBlocks, width: 96, height: 64);

    [Benchmark]
    public string Lists()
        => Render(_lists, width: 80, height: 64);

    [Benchmark]
    public string RepeatedSameWidth()
        => Render(_longMarkdown, width: 88, height: 64);

    [Benchmark]
    public string ChangingWidths()
    {
        ReadOnlySpan<int> widths = [48, 72, 96, 120];
        var width = widths[_widthIndex++ % widths.Length];
        return Render(_longMarkdown, width, height: 64);
    }

    private static string Render(MarkdownComponent markdown, int width, int height)
        => TuiCapture.RenderToString(markdown, width, height, trimTrailingBlankLines: false);

    private static string BuildLongMarkdown()
        => string.Join("\n\n", Enumerable.Range(0, 200).Select(i =>
            $"## Section {i:D3}\n\nThis paragraph has enough text to wrap across multiple terminal columns. {new string('x', 160)}"));

    private static string BuildCodeBlocks()
        => string.Join("\n\n", Enumerable.Range(0, 80).Select(i =>
            $"```csharp\nvar value{i:D3} = \"{new string('x', 120)}\";\nConsole.WriteLine(value{i:D3});\n```"));

    private static string BuildLists()
        => string.Join('\n', Enumerable.Range(0, 500).Select(i =>
            $"- item {i:D3} with wrapped text {new string('x', 120)}"));
}
