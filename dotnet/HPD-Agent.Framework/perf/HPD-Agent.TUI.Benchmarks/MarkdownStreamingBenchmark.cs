using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
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
    public MarkdownProjectionDiagnosticsSnapshot StablePrefixMutableTail() => StreamAndLayout(Representative, 32, [80]);

    [Benchmark]
    public MarkdownLayout LongCodeBlock() => FinalLayout(LongCode, 96);

    [Benchmark]
    public MarkdownProjectionDiagnosticsSnapshot GrowingTables() => StreamAndLayout(GrowingTable, 24, [100], serviceEveryCompletedLine: 16);

    [Benchmark]
    public string LongTranscriptActiveMessage()
    {
        var model = new TranscriptModel();
        for (var index = 0; index < 1_000; index++)
            model.AddFinal(new TranscriptEntry($"row-{index}", null, new UserMessageCell($"row {index} {new string('x', 80)}"), new()));
        var session = CompletedSession(Representative);
        var document = session.Complete().Document;
        _ = session.Projection.Prepare(document, Options(100), new MarkdownLayoutEngine());
        model.UpsertLive(new TranscriptEntry("active", "assistant:benchmark",
            new AssistantMessageCell("assistant", document, session.Projection), new()));
        var registry = new HpdAgentTuiBuilder().AddDefaultTranscriptRenderers().Build().TranscriptRenderers;
        return TuiCapture.RenderToString(new TranscriptView(model, registry, 24), 100, 24,
            trimTrailingBlankLines: false);
    }

    [Benchmark]
    public MarkdownProjectionDiagnosticsSnapshot RepeatedResizePublicationSource()
    {
        var session = CompletedSession(Representative);
        var document = session.Complete().Document;
        var engine = new MarkdownLayoutEngine();
        foreach (var width in Enumerable.Repeat(new[] { 48, 72, 96, 120 }, 4).SelectMany(static widths => widths))
            _ = session.Projection.Prepare(document, Options(width), engine);
        return session.Projection.Diagnostics;
    }

    [Benchmark]
    public MarkdownLayout VeryLargeAdversarialMessage() => FinalLayout(Adversarial, 80);

    [Benchmark]
    public MarkdownProjectionDiagnosticsSnapshot OverBudgetEventLoopWorkload()
    {
        var dispatcher = new BenchmarkDispatcher();
        MarkdownMessageProjection? published = null;
        var coordinator = new MarkdownStreamCoordinator(dispatcher, (update, projection) =>
        {
            _ = projection.Prepare(update.Document, Options(80), new MarkdownLayoutEngine());
            published = projection;
        });
        var identity = new MarkdownStreamIdentity(MarkdownStreamKind.Assistant, "event-loop");
        coordinator.Start(identity);
        foreach (var character in Representative) coordinator.Append(identity, character.ToString());
        coordinator.Complete(identity);
        dispatcher.Drain();
        return published!.Diagnostics;
    }

    private static MarkdownProjectionDiagnosticsSnapshot StreamAndLayout(
        string source,
        int deltaLength,
        int[] widths,
        int serviceEveryCompletedLine = 1)
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "layout"));
        var engine = new MarkdownLayoutEngine();
        var completedLines = 0;
        for (var offset = 0; offset < source.Length; offset += deltaLength)
        {
            var delta = source.Substring(offset, Math.Min(deltaLength, source.Length - offset));
            if (!session.Append(delta).CompletedPhysicalLine || ++completedLines % serviceEveryCompletedLine != 0) continue;
            var document = session.Refresh().Document;
            foreach (var width in widths) _ = session.Projection.Prepare(document, Options(width), engine);
        }
        var final = session.Complete().Document;
        foreach (var width in widths) _ = session.Projection.Prepare(final, Options(width), engine);
        return session.Projection.Diagnostics;
    }

    private static MarkdownLayout FinalLayout(string source, int width)
    {
        var session = CompletedSession(source);
        var document = session.Complete().Document;
        return session.Projection.Prepare(document, Options(width), new MarkdownLayoutEngine());
    }

    private static MarkdownStreamSession CompletedSession(string source)
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "complete"));
        session.Append(source);
        return session;
    }

    private static MarkdownLayoutOptions Options(int width) =>
        new(width, MarkdownTheme.FromTheme(Theme.Default));

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

    private sealed class BenchmarkDispatcher : IAgentTuiDispatcher
    {
        private readonly Queue<Action> _queue = [];
        public bool CheckAccess() => true;
        public void Post(Action callback) => _queue.Enqueue(callback);
        public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default)
        { Post(callback); return ValueTask.CompletedTask; }
        public ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default)
        { Post(() => callback().GetAwaiter().GetResult()); return ValueTask.CompletedTask; }
        public void Drain() { while (_queue.TryDequeue(out var callback)) callback(); }
    }
}
