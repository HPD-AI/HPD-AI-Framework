using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace HPD.Agent.TUI.Benchmarks;

/// <summary>Measures the proposal's distinct streaming parser and publication workloads.</summary>
[MemoryDiagnoser]
public class MarkdownStreamingBenchmark
{
    private long _lastEventLoopP50Microseconds;
    private long _lastEventLoopP95Microseconds;
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
        _transcriptRenderers = new HpdAgentTuiBuilder().AddDefaultTranscriptRenderers().Build().TranscriptRenderers;
        _plainHistory = new TranscriptModel();
        _sourceBackedHistory = new TranscriptModel();
        _activeHistory = new TranscriptModel();
        var finalizedSession = CompletedSession("finalized **source-backed** row");
        var finalizedDocument = finalizedSession.Complete().Document;
        _ = finalizedSession.Projection.Prepare(finalizedDocument, Options(100), new MarkdownLayoutEngine());
        for (var index = 0; index < 1_000; index++)
        {
            var text = $"row {index} {new string('x', 80)}";
            _plainHistory.AddFinal(new TranscriptEntry($"plain-{index}", null, new UserMessageCell(text), new()));
            _activeHistory.AddFinal(new TranscriptEntry($"active-final-{index}", null, new UserMessageCell(text), new()));
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
            new AssistantMessageCell("assistant", document, activeSession.Projection), new()));
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
    public long SerializedEventLoopOverloadP95Microseconds()
    {
        using var dispatcher = new SerializedBenchmarkDispatcher(capacity: 256);
        MarkdownMessageProjection? published = null;
        var coordinator = new MarkdownStreamCoordinator(dispatcher, (update, projection) =>
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
            if (++chunks % 64 == 0) dispatcher.Flush();
        }
        coordinator.Complete(identity);
        dispatcher.Flush();
        _ = published!.Diagnostics;
        _lastEventLoopP50Microseconds = dispatcher.P50Microseconds;
        _lastEventLoopP95Microseconds = dispatcher.P95Microseconds;
        return _lastEventLoopP95Microseconds;
    }

    [GlobalCleanup]
    public void ReportEventLoopPercentiles()
    {
        if (_lastEventLoopP95Microseconds > 0)
            Console.WriteLine($"MARKDOWN_EVENT_LOOP_LATENCY p50={_lastEventLoopP50Microseconds}us p95={_lastEventLoopP95Microseconds}us");
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

    private sealed class SerializedBenchmarkDispatcher : IAgentTuiDispatcher, IDisposable
    {
        private readonly BlockingCollection<(Action Callback, long QueuedAt)> _queue;
        private readonly System.Threading.Thread _thread;
        private readonly List<long> _latencies = [];
        private readonly ManualResetEventSlim _idle = new(initialState: true);
        private int _pending;
        private int _threadId;

        public SerializedBenchmarkDispatcher(int capacity)
        {
            _queue = new(capacity);
            _thread = new System.Threading.Thread(Run) { IsBackground = true, Name = "markdown-benchmark-dispatcher" };
            _thread.Start();
            while (Volatile.Read(ref _threadId) == 0) System.Threading.Thread.Yield();
        }

        public bool CheckAccess() => System.Environment.CurrentManagedThreadId == _threadId;
        public void Post(Action callback)
        {
            Interlocked.Increment(ref _pending);
            _idle.Reset();
            _queue.Add((callback, Stopwatch.GetTimestamp()));
        }
        public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default)
        { Post(callback); return ValueTask.CompletedTask; }
        public ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default)
        { Post(() => callback().GetAwaiter().GetResult()); return ValueTask.CompletedTask; }
        public long P50Microseconds => PercentileMicroseconds(.50);
        public long P95Microseconds => PercentileMicroseconds(.95);
        public void Flush() => _idle.Wait();
        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
            _idle.Dispose();
            _queue.Dispose();
        }
        private void Run()
        {
            Volatile.Write(ref _threadId, System.Environment.CurrentManagedThreadId);
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                _latencies.Add(Stopwatch.GetElapsedTime(work.QueuedAt).Ticks);
                try { work.Callback(); }
                finally
                {
                    if (Interlocked.Decrement(ref _pending) == 0) _idle.Set();
                }
            }
        }

        private long PercentileMicroseconds(double percentile)
        {
            var ordered = _latencies.Order().ToArray();
            return ordered.Length == 0 ? 0 : ordered[(int)Math.Ceiling(ordered.Length * percentile) - 1] * 1_000_000 / TimeSpan.TicksPerSecond;
        }
    }
}
