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

public sealed record TuiRenderCompleted(
    string Surface,
    TimeSpan Duration,
    int RowsRendered,
    int SegmentsWritten,
    int CacheHits,
    int CacheMisses) : HpdTuiPerformanceEvent
{
    public override string FormatSummary()
        => $"tui frame {Duration.TotalMilliseconds:0.###}ms surface={Surface} rows={RowsRendered} segments={SegmentsWritten} cache={CacheHits}/{CacheMisses}";
}
