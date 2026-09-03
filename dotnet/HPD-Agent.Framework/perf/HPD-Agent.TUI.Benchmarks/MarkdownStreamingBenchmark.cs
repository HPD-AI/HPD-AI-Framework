using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;
using System.Text;

namespace HPD.Agent.TUI.Benchmarks;

/// <summary>Measures the proposal's distinct streaming parser and publication workloads.</summary>
[MemoryDiagnoser]
public class MarkdownStreamingBenchmark
{
    private static readonly string Representative = string.Join("\n\n", Enumerable.Range(0, 80)
        .Select(index => $"## Section {index}\n\nParagraph {index} {new string('x', 80)}"));
    private static readonly string LongCode = $"```csharp\n{new string('x', 64_000)}\n```";
    private static readonly string GrowingTable = "| Key | Value |\n|---|---|\n" +
        string.Join('\n', Enumerable.Range(0, 500).Select(index => $"| {index} | {new string('v', 40)} |"));
    private static readonly string Adversarial = string.Concat(Enumerable.Repeat("`*_[]()|<>&", 4_000));

    [Benchmark(Baseline = true)]
    public int FullMessagePerDelta()
    {
        var parser = new MarkdownDocumentParser();
        var options = new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() };
        var source = new StringBuilder();
        var checksum = 0;
        for (var offset = 0; offset < Representative.Length; offset += 8)
        {
            source.Append(Representative, offset, Math.Min(8, Representative.Length - offset));
            checksum += parser.Parse(source.ToString(), options).Blocks.Count;
        }
        return checksum;
    }

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot CoalescedFullMessage() => Stream(Representative, 8, refreshEveryDelta: false);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot NewlineGated() => Stream(Representative, 8, refreshEveryDelta: false, refreshOnNewline: true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot StablePrefixMutableTail() => Stream(Representative, 32, refreshEveryDelta: false, refreshOnNewline: true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot LongCodeBlock() => Stream(LongCode, 64, refreshEveryDelta: false, refreshOnNewline: true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot GrowingTables() => Stream(GrowingTable, 24, refreshEveryDelta: false, refreshOnNewline: true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot LongTranscriptActiveMessage() => Stream(Representative + Representative, 64, false, true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot RepeatedResizePublicationSource() => Stream(Representative, 128, false, true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot VeryLargeAdversarialMessage() => Stream(Adversarial, 256, false, true);

    [Benchmark]
    public MarkdownStreamDiagnosticsSnapshot OverBudgetEventLoopWorkload() => Stream(Representative, 1, false, true);

    private static MarkdownStreamDiagnosticsSnapshot Stream(
        string source,
        int deltaLength,
        bool refreshEveryDelta,
        bool refreshOnNewline = false)
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "benchmark"));
        for (var offset = 0; offset < source.Length; offset += deltaLength)
        {
            var length = Math.Min(deltaLength, source.Length - offset);
            var delta = source.Substring(offset, length);
            var change = session.Append(delta);
            if (refreshEveryDelta || refreshOnNewline && change.CompletedPhysicalLine) _ = session.Refresh();
        }
        _ = session.Complete();
        return session.Diagnostics;
    }
}
