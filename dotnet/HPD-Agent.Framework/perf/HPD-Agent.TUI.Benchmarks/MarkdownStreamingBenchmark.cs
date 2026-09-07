using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;
using HPD.TUI.Core;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using System.Text;
using System.Diagnostics;

namespace HPD.Agent.TUI.Benchmarks;

/// <summary>Measures the proposal's distinct streaming parser and publication workloads.</summary>
[MemoryDiagnoser]
public class MarkdownStreamingBenchmark
{
    private long _lastWorkerP50Microseconds;
    private long _lastWorkerP95Microseconds;
    private TranscriptModel _plainHistory = null!;
    private TranscriptModel _sourceBackedHistory = null!;
    private TranscriptModel _activeHistory = null!;
    private AgentTuiTranscriptRendererRegistry _transcriptRenderers = null!;
    private int _activeRevision;
    private static readonly string Representative = string.Join("\n\n", Enumerable.Range(0, 80)
        .Select(index => $"## Section {index}\n\nParagraph {index} {new string('x', 80)}"));
    private static readonly string LongCode = $"```csharp\n{new string('x', 64_000)}\n```";
    private static readonly string GrowingTable = "| Key | Value |\n|---|---|\n" +
        string.Join('\n', Enumerable.Range(0, 500).Select(index => $"| {index} | {new string('v', 40)} |"));
    private static readonly string Adversarial = string.Concat(Enumerable.Repeat("`*_[]()|<>&", 4_000));

    [GlobalSetup]
    public void PrepareTranscriptFixtures()
    {
        _transcriptRenderers = new HpdAgentTuiBuilder().AddDefaultTranscriptRenderers()
            .AddTranscriptRenderer<LegacyAssistantCell>("benchmark-legacy-assistant", context =>
                context.Services.Prefix(context.Cell.Body, context.DepthIndent, context.DepthIndent))
            .Build().TranscriptRenderers;
        _plainHistory = new TranscriptModel();
        _sourceBackedHistory = new TranscriptModel();
        _activeHistory = new TranscriptModel();
        for (var index = 0; index < 1_000; index++)
        {
            var text = $"row {index} {new string('x', 80)}";
            _plainHistory.AddFinal(new TranscriptEntry($"plain-{index}", null,
                new LegacyAssistantCell(new Text(text)), new()));
            _activeHistory.AddFinal(new TranscriptEntry($"active-final-{index}", null, new UserMessageCell(text), new()));
            var finalizedSession = CompletedSession(text);
            var finalizedDocument = finalizedSession.Complete().Document;
            _ = finalizedSession.Projection.Prepare(finalizedDocument, Options(100), new MarkdownLayoutEngine());
            _sourceBackedHistory.AddFinal(new TranscriptEntry($"markdown-{index}", null,
                new AssistantMessageCell("assistant", finalizedDocument, finalizedSession.Projection), new()));
        }
    }

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
    public string FinalizedHistoryPlainTextBaseline() =>
        TuiCapture.RenderToString(new TranscriptView(_plainHistory, _transcriptRenderers, 24), 100, 24,
            trimTrailingBlankLines: false);

    [Benchmark]
    public string FinalizedHistorySourceBacked() =>
        TuiCapture.RenderToString(new TranscriptView(_sourceBackedHistory, _transcriptRenderers, 24), 100, 24,
            trimTrailingBlankLines: false);

    [Benchmark]
    public string IsolatedActiveMessageUpdateWith1000FinalizedRows()
    {
        var activeSession = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, $"active-isolated-{_activeRevision++}"));
        activeSession.Append("active message update");
        var document = activeSession.Complete().Document;
        _ = activeSession.Projection.Prepare(document, Options(100), new MarkdownLayoutEngine());
        _activeHistory.UpsertLive(new TranscriptEntry("active", "assistant:benchmark",
            new AssistantMessageCell("assistant", document, activeSession.Projection), new()),
            CommittedHistoryMutationPolicy.Reject);
        return TuiCapture.RenderToString(new TranscriptView(_activeHistory, _transcriptRenderers, 24), 100, 24,
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
    public long WorkerBatchP95Microseconds()
    {
        var samples = new List<long>();
        MarkdownMessageProjection? published = null;
        var coordinator = new MarkdownStreamCoordinator((update, projection) =>
        {
            _ = projection.Prepare(update.Document, Options(80), new MarkdownLayoutEngine());
            published = projection;
        });
        var identity = new MarkdownStreamIdentity(MarkdownStreamKind.Assistant, "event-loop");
        coordinator.Start(identity);
        var chunks = 0;
        for (var offset = 0; offset < Representative.Length; offset += 8)
        {
            coordinator.Append(identity, Representative.Substring(offset, Math.Min(8, Representative.Length - offset)));
            if (++chunks % 32 == 0)
            {
                var started = Stopwatch.GetTimestamp();
                coordinator.RefreshPending();
                samples.Add(Stopwatch.GetElapsedTime(started).Ticks);
            }
        }
        coordinator.Complete(identity);
        _ = published!.Diagnostics;
        samples.Sort();
        _lastWorkerP50Microseconds = PercentileMicroseconds(samples, .50);
        _lastWorkerP95Microseconds = PercentileMicroseconds(samples, .95);
        return _lastWorkerP95Microseconds;
    }

    [GlobalCleanup]
    public void ReportWorkerPercentiles()
    {
        if (_lastWorkerP95Microseconds > 0)
            Console.WriteLine($"MARKDOWN_WORKER_BATCH p50={_lastWorkerP50Microseconds}us p95={_lastWorkerP95Microseconds}us");
    }

    private static long PercentileMicroseconds(List<long> samples, double percentile) =>
        samples.Count == 0 ? 0 : samples[(int)Math.Ceiling(samples.Count * percentile) - 1] * 1_000_000 / TimeSpan.TicksPerSecond;

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

    private sealed record LegacyAssistantCell(IComponent Body) : TranscriptCell;

}
