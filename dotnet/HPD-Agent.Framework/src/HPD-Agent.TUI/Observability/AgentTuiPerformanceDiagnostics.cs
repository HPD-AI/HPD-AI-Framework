using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Runtime;
using HPD.Events;
using HPD.Agent.Serialization;
using HPD.TUI.Observability;
using HPD.Agent.TUI.Markdown;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Observability;

public static class AgentTuiPerformanceDiagnostics
{
    public const string EnvironmentVariableName = "HPD_TUI_PERF";
    public const string SinkStateKey = "hpd.agent-tui.performance.sink";

    public static void SetSink(AgentTuiStateBag state, IHpdTuiPerformanceEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sink);

        state.Set(SinkStateKey, sink);
    }

    public static void SetSink(AgentTuiStateBag state, IEventPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        SetSink(state, new EventPublisherTuiPerformanceEventSink(publisher));
    }

    public static bool RemoveSink(AgentTuiStateBag state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Remove(SinkStateKey);
    }

    public static bool TryGetSink(AgentTuiStateBag state, out IHpdTuiPerformanceEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.TryGet(SinkStateKey, out sink);
    }

    public static bool ConfigureFromEnvironment(AgentTuiStateBag state)
        => ConfigureFromEnvironment(
            state,
            global::System.Environment.GetEnvironmentVariable,
            Console.Error);

    internal static bool ConfigureFromEnvironment(
        AgentTuiStateBag state,
        Func<string, string?> getEnvironmentVariable,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TuiPerformanceDiagnostics.IsEnabled(getEnvironmentVariable(EnvironmentVariableName)))
        {
            return false;
        }

        SetSink(state, new TextWriterTuiPerformanceEventSink(writer));
        return true;
    }
}

public abstract record AgentTuiPerformanceEvent : AgentEvent, IObservabilityEvent, IHpdTuiPerformanceSummary
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public override EventChannel Channel { get; init; } = EventChannel.Streaming;


    public virtual string FormatSummary()
        => $"tui {GetType().Name} kind={Kind} channel={Channel}";
}

[EventType("TRANSCRIPT_VIEW_RENDERED")]
public sealed record TranscriptViewRendered(
    string? AgentId,
    int EntriesVisited,
    int RowsCaptured,
    int RowsRendered,
    int CacheHits,
    int CacheMisses,
    TimeSpan Duration) : AgentTuiPerformanceEvent
{
    public override string FormatSummary()
        => $"transcript render {Duration.TotalMilliseconds:0.###}ms rows={RowsRendered} captured={RowsCaptured} visited={EntriesVisited} cache={CacheHits}/{CacheMisses}";
}

[EventType("AGENT_TUI_EVENT_BATCH_APPLIED")]
public sealed record AgentTuiEventBatchApplied(
    string? AgentId,
    AgentTuiEventDeliveryMode DeliveryMode,
    int EventCount,
    ThreadJournalCursor FirstCursor,
    ThreadJournalCursor LastCursor,
    TimeSpan Duration) : AgentTuiPerformanceEvent
{
    public override string FormatSummary()
        => $"event batch {DeliveryMode} {Duration.TotalMilliseconds:0.###}ms events={EventCount} cursor={FirstCursor.Generation}:{FirstCursor.SequenceNumber}-{LastCursor.Generation}:{LastCursor.SequenceNumber}";
}

[EventType("MARKDOWN_PROJECTION_MEASURED")]
public sealed record MarkdownProjectionMeasured(
    string? AgentId,
    string MessageId,
    MarkdownStreamKind StreamKind,
    MarkdownMessageState State,
    MarkdownInvalidationKind Invalidation,
    MarkdownDegradationReason DegradationReason,
    MarkdownStreamDiagnosticsSnapshot Stream,
    MarkdownProjectionDiagnosticsSnapshot Projection) : AgentTuiPerformanceEvent
{
    public override string FormatSummary()
        => $"markdown projection state={State} invalidation={Invalidation} degradation={DegradationReason} " +
           $"deltas={Stream.DeltasAccepted} coalesced={Stream.DeltasCoalesced} parses={Stream.ParseCount} " +
           $"layouts={Projection.LayoutCount} reuse={Projection.StableBlocksReused} " +
           $"cache={Projection.CacheHits}/{Projection.CacheMisses}/{Projection.CacheEvictions}";
}
