using HPD.Events;

namespace HPD.TUI.Observability;

public interface IHpdTuiPerformanceEventSink
{
    void Publish(Event evt);
}

public interface IHpdTuiPerformanceSummary
{
    string FormatSummary();
}

public static class TuiPerformanceDiagnostics
{
    public const string EnvironmentVariableName = "HPD_TUI_PERF";

    public static bool IsEnabled(string? value)
        => value is not null &&
           (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));

    public static IHpdTuiPerformanceEventSink? CreateTextWriterSinkFromEnvironment(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return IsEnabled(global::System.Environment.GetEnvironmentVariable(EnvironmentVariableName))
            ? new TextWriterTuiPerformanceEventSink(writer)
            : null;
    }
}

public sealed class EventPublisherTuiPerformanceEventSink : IHpdTuiPerformanceEventSink
{
    private readonly IEventPublisher _publisher;

    public EventPublisherTuiPerformanceEventSink(IEventPublisher publisher)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public void Publish(Event evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _publisher.Emit(evt);
    }
}

public sealed class TextWriterTuiPerformanceEventSink : IHpdTuiPerformanceEventSink
{
    private readonly TextWriter _writer;

    public TextWriterTuiPerformanceEventSink(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Publish(Event evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var summary = evt is IHpdTuiPerformanceSummary performanceSummary
            ? performanceSummary.FormatSummary()
            : $"event {evt.GetType().Name} kind={evt.Kind} channel={evt.Channel}";
        _writer.WriteLine(summary);
    }
}

public abstract record HpdTuiPerformanceEvent : Event, IHpdTuiPerformanceSummary
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public override EventChannel Channel { get; init; } = EventChannel.Streaming;

    public virtual string FormatSummary()
        => $"tui {GetType().Name} kind={Kind} channel={Channel}";
}

/// <summary>Describes measured work performed by one admitted compositor frame.</summary>
/// <param name="SchedulingDelay">Time between the requested and admitted frame, when supplied by the application scheduler.</param>
/// <param name="LayoutDuration">Time spent in a distinct layout phase; zero when layout is performed inside display-list preparation.</param>
/// <param name="DisplayListDuration">Time spent resolving or building the retained display list.</param>
/// <param name="RasterDuration">Time spent replaying commands and fingerprinting the physical screen.</param>
/// <param name="DiffDuration">Time spent semantically comparing rows and locating changed runs.</param>
/// <param name="EncodeDuration">Time spent encoding terminal output outside semantic comparison.</param>
/// <param name="OutputDuration">Time spent attempting transport publication.</param>
/// <param name="ComponentsMeasured">Components measured by a separately instrumented layout phase.</param>
/// <param name="ComponentsPainted">Components whose display-list slices were rebuilt.</param>
/// <param name="DisplayCommandsReused">Retained commands reused by the frame.</param>
/// <param name="DisplayCommandsBuilt">Commands built by the frame.</param>
/// <param name="RowsDamaged">Physical rows conservatively damaged by display-list changes.</param>
/// <param name="RowsFingerprintRejected">Rows rejected as unchanged by semantic fingerprints and equality.</param>
/// <param name="RowsSemanticallyCompared">Rows subjected to semantic comparison.</param>
/// <param name="ChangedRuns">Disjoint changed runs encoded by the frame.</param>
/// <param name="CellsCompared">Cells covered by rows subjected to semantic comparison.</param>
/// <param name="CellsChanged">Cells covered by changed runs.</param>
/// <param name="OutputCharacters">Encoded characters offered to the transport.</param>
/// <param name="FullRepaint">Whether publication rebuilt the complete physical screen.</param>
/// <param name="Backpressured">Whether the transport accepted zero characters due to backpressure.</param>
public sealed record TuiFrameDiagnostics(
    TimeSpan SchedulingDelay,
    TimeSpan LayoutDuration,
    TimeSpan DisplayListDuration,
    TimeSpan RasterDuration,
    TimeSpan DiffDuration,
    TimeSpan EncodeDuration,
    TimeSpan OutputDuration,
    int ComponentsMeasured,
    int ComponentsPainted,
    int DisplayCommandsReused,
    int DisplayCommandsBuilt,
    int RowsDamaged,
    int RowsFingerprintRejected,
    int RowsSemanticallyCompared,
    int ChangedRuns,
    int CellsCompared,
    int CellsChanged,
    int OutputCharacters,
    bool FullRepaint,
    bool Backpressured) : HpdTuiPerformanceEvent
{
    /// <inheritdoc />
    public override string FormatSummary()
        => $"tui frame layout={LayoutDuration.TotalMilliseconds:0.###}ms display={DisplayListDuration.TotalMilliseconds:0.###}ms raster={RasterDuration.TotalMilliseconds:0.###}ms diff={DiffDuration.TotalMilliseconds:0.###}ms output={OutputDuration.TotalMilliseconds:0.###}ms damage={RowsDamaged} runs={ChangedRuns} cells={CellsChanged} commands={DisplayCommandsReused}/{DisplayCommandsBuilt} chars={OutputCharacters} full={FullRepaint} backpressured={Backpressured}";
}

/// <summary>Reports actual frame-admission decisions made by an application mailbox.</summary>
/// <param name="RenderRequestsReceived">Render requests removed from the FIFO mailbox.</param>
/// <param name="RenderRequestsCoalesced">Requests deliberately collapsed by the active frame policy.</param>
/// <param name="FramesAdmitted">Frames passed to the renderer.</param>
/// <param name="FramesDeferredByPacing">Admissions delayed until a frame deadline.</param>
/// <param name="FramesDeferredByBackpressure">Admissions delayed until transport writability.</param>
public sealed record TuiSchedulingDiagnostics(
    long RenderRequestsReceived,
    long RenderRequestsCoalesced,
    long FramesAdmitted,
    long FramesDeferredByPacing,
    long FramesDeferredByBackpressure) : HpdTuiPerformanceEvent
{
    /// <inheritdoc />
    public override string FormatSummary()
        => $"tui scheduling requests={RenderRequestsReceived} coalesced={RenderRequestsCoalesced} admitted={FramesAdmitted} pacing={FramesDeferredByPacing} backpressure={FramesDeferredByBackpressure}";
}

/// <summary>
/// Immutable aggregate operation counters shared by the compositor, scheduler, retained surfaces,
/// scrollback journal, and incremental Markdown pipeline.
/// </summary>
/// <remarks>Values are cumulative and monotonic for the lifetime of their recorder.</remarks>
public readonly record struct TuiPerformanceCounterSnapshot(
    long RenderRequestsReceived,
    long RenderRequestsCoalesced,
    long FramesAdmitted,
    long FramesSuppressedAsNoOp,
    long FramesDeferredByPacing,
    long FramesDeferredByBackpressure,
    long ComponentsMeasured,
    long LayoutCacheHits,
    long LayoutCacheMisses,
    long SurfaceCacheBytes,
    long SurfaceCacheEvictions,
    long ScrollbackRowsCommitted,
    long ScrollbackRowsReplayed,
    long MarkdownStablePrefixNodesReused,
    long MarkdownCharactersReparsed);

/// <summary>
/// Thread-safe recorder for the common TUI operation-counter contract.
/// </summary>
/// <remarks>
/// Diagnostics are disabled by leaving the recorder reference <see langword="null"/>. Producers
/// must null-check before recording, so the disabled hot path neither creates payload objects nor
/// formats strings.
/// </remarks>
public sealed class TuiPerformanceCounters
{
    private long _renderRequestsReceived;
    private long _renderRequestsCoalesced;
    private long _framesAdmitted;
    private long _framesSuppressedAsNoOp;
    private long _framesDeferredByPacing;
    private long _framesDeferredByBackpressure;
    private long _componentsMeasured;
    private long _layoutCacheHits;
    private long _layoutCacheMisses;
    private long _surfaceCacheBytes;
    private long _surfaceCacheEvictions;
    private long _scrollbackRowsCommitted;
    private long _scrollbackRowsReplayed;
    private long _markdownStablePrefixNodesReused;
    private long _markdownCharactersReparsed;

    /// <summary>Returns a consistent field-by-field snapshot of the cumulative counters.</summary>
    public TuiPerformanceCounterSnapshot Snapshot() => new(
        Interlocked.Read(ref _renderRequestsReceived),
        Interlocked.Read(ref _renderRequestsCoalesced),
        Interlocked.Read(ref _framesAdmitted),
        Interlocked.Read(ref _framesSuppressedAsNoOp),
        Interlocked.Read(ref _framesDeferredByPacing),
        Interlocked.Read(ref _framesDeferredByBackpressure),
        Interlocked.Read(ref _componentsMeasured),
        Interlocked.Read(ref _layoutCacheHits),
        Interlocked.Read(ref _layoutCacheMisses),
        Interlocked.Read(ref _surfaceCacheBytes),
        Interlocked.Read(ref _surfaceCacheEvictions),
        Interlocked.Read(ref _scrollbackRowsCommitted),
        Interlocked.Read(ref _scrollbackRowsReplayed),
        Interlocked.Read(ref _markdownStablePrefixNodesReused),
        Interlocked.Read(ref _markdownCharactersReparsed));

    internal void RecordRenderRequest(bool coalesced)
    {
        Interlocked.Increment(ref _renderRequestsReceived);
        if (coalesced) Interlocked.Increment(ref _renderRequestsCoalesced);
    }

    internal void RecordFrameAdmitted() => Interlocked.Increment(ref _framesAdmitted);
    internal void RecordFrameSuppressed() => Interlocked.Increment(ref _framesSuppressedAsNoOp);
    internal void RecordPacingDeferral() => Interlocked.Increment(ref _framesDeferredByPacing);
    internal void RecordBackpressureDeferral() => Interlocked.Increment(ref _framesDeferredByBackpressure);

    internal void RecordLayoutMeasurement(bool cacheHit)
    {
        if (cacheHit) Interlocked.Increment(ref _layoutCacheHits);
        else
        {
            Interlocked.Increment(ref _layoutCacheMisses);
            Interlocked.Increment(ref _componentsMeasured);
        }
    }

    /// <summary>Records bytes newly owned by a retained-surface cache.</summary>
    public void RecordSurfaceAllocation(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Interlocked.Add(ref _surfaceCacheBytes, bytes);
    }

    /// <summary>Records one retained-surface eviction and the bytes it released.</summary>
    public void RecordSurfaceEviction(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Interlocked.Increment(ref _surfaceCacheEvictions);
        Interlocked.Add(ref _surfaceCacheBytes, -bytes);
    }

    /// <summary>Records rows durably admitted to terminal scrollback.</summary>
    public void RecordScrollbackCommitted(long rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        Interlocked.Add(ref _scrollbackRowsCommitted, rows);
    }

    /// <summary>Records rows replayed while recovering terminal scrollback.</summary>
    public void RecordScrollbackReplayed(long rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        Interlocked.Add(ref _scrollbackRowsReplayed, rows);
    }

    /// <summary>Records proven-stable Markdown nodes reused and UTF-16 characters reparsed.</summary>
    public void RecordMarkdownWork(long stablePrefixNodes, long reparsedCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stablePrefixNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(reparsedCharacters);
        Interlocked.Add(ref _markdownStablePrefixNodesReused, stablePrefixNodes);
        Interlocked.Add(ref _markdownCharactersReparsed, reparsedCharacters);
    }
}
