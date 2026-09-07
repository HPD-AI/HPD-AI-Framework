using BenchmarkDotNet.Attributes;
using HPD.TUI.Rendering;
using HPD.TUI.Core;
using HPD.TUI.Markdown;
using MarkdownComponent = HPD.TUI.Content.MarkdownBlock;

namespace HPD.TUI.Benchmarks;

[MemoryDiagnoser]
public class MarkdownRenderBenchmark
{
    private static readonly string LongSource = BuildLongMarkdown();
    private readonly MarkdownComponent _shortMarkdown = MarkdownComponent.Prepare("# Title\n\nA short paragraph with **bold** and `code`.", 80, Theme.Default);
    private readonly MarkdownComponent _longMarkdown = MarkdownComponent.Prepare(LongSource, 100, Theme.Default);
    private readonly MarkdownComponent _longRepeated = MarkdownComponent.Prepare(LongSource, 88, Theme.Default);
    private readonly MarkdownComponent _codeBlocks = MarkdownComponent.Prepare(BuildCodeBlocks(), 96, Theme.Default);
    private readonly MarkdownComponent _lists = MarkdownComponent.Prepare(BuildLists(), 80, Theme.Default);
    private readonly MarkdownComponent[] _changingWidths = new[] { 48, 72, 96, 120 }
        .Select(width => MarkdownComponent.Prepare(LongSource, width, Theme.Default)).ToArray();
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
        => Render(_longRepeated, width: 88, height: 64);

    [Benchmark]
    public string ChangingWidths()
    {
        ReadOnlySpan<int> widths = [48, 72, 96, 120];
        var index = _widthIndex++ % widths.Length;
        return Render(_changingWidths[index], widths[index], height: 64);
    }

    [Benchmark]
    public MarkdownLayout PublicationParseAndLayout()
    {
        var pipeline = MarkdownPipelineFactory.CreateDefault();
        var document = new MarkdownDocumentParser().Parse(LongSource, new MarkdownParseOptions { Pipeline = pipeline });
        return new MarkdownLayoutEngine().Layout(document, new(88, MarkdownTheme.FromTheme(Theme.Default)));
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
