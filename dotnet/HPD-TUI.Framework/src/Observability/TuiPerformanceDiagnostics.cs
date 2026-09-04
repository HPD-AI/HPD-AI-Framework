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

/// <summary>Describes the work performed by one admitted compositor frame.</summary>
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
