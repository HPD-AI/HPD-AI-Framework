using HPD.Events;
using Rhodium.Primitives;

namespace Rhodium.Events;

/// <summary>
/// Base class for all finance events. Inherits from HPD.Events.Event for
/// channel routing, stream management, and hierarchical coordination.
/// </summary>
public abstract record FinanceEvent : Event
{
    /// <summary>
    /// Finance-specific timestamp with nanosecond precision.
    /// Bridges HPD.Events.Timestamp (DateTimeOffset) and ExchangeTimestampNs to Finance Instant.
    /// </summary>
    public Instant Time
    {
        get => ExchangeTimestampNs != 0
            ? new Instant(ExchangeTimestampNs)
            : Instant.FromDateTimeOffset(Timestamp);
        init
        {
            Timestamp = value.ToDateTimeOffset();
            ExchangeTimestampNs = value.Nanos;
        }
    }

    /// <summary>
    /// Sequence number for event ordering and replay.
    /// Assigned by the event coordinator, not the event producer.
    /// </summary>
    public Sequence? Sequence { get; init; }
}

/// <summary>
/// Market data events (quotes, bars, trades, book updates).
/// Streaming channel - high-throughput feed data.
/// </summary>
public abstract record MarketEvent : FinanceEvent
{
    public abstract Instrument Instrument { get; }

    public override EventKind Kind => EventKind.Content;
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>
/// Execution events (fills, rejects, cancellations).
/// Synchronous channel - causally ordered account state.
/// </summary>
public abstract record ExecutionEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Content;
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>
/// Control events (user cancellation, risk limits).
/// Control channel - interruptions and circuit breakers.
/// </summary>
public abstract record ControlEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Control;
    public override EventChannel Channel => EventChannel.Control;
}

/// <summary>
/// Lifecycle events (session start/end, market open/close).
/// </summary>
public abstract record LifecycleEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Lifecycle;
    public override EventChannel Channel => EventChannel.Synchronous;
}

/// <summary>
/// Diagnostic events (metrics, performance snapshots).
/// Streaming channel - high-volume observability data.
/// </summary>
public abstract record DiagnosticEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Diagnostic;
    public override EventChannel Channel => EventChannel.Streaming;
}
