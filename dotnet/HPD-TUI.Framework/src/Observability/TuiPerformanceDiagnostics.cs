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
