using HPD.Events;
using Rhodium.Primitives;

namespace Rhodium.Events;

/// <summary>
/// Base class for all finance events. Inherits from HPD.Events.Event for
/// priority routing, stream management, and hierarchical coordination.
/// </summary>
public abstract record FinanceEvent : Event
{
    /// <summary>
    /// Finance-specific timestamp with nanosecond precision.
    /// Bridges HPD.Events.Timestamp (DateTimeOffset) to Finance Instant.
    /// </summary>
    public Instant Time => Instant.FromDateTimeOffset(Timestamp);

    /// <summary>
    /// Sequence number for event ordering and replay.
    /// Assigned by the event coordinator, not the event producer.
    /// </summary>
    public Sequence? Sequence { get; init; }
}

/// <summary>
/// Market data events (quotes, bars, trades, book updates).
/// Normal priority - processed in order during live trading.
/// </summary>
public abstract record MarketEvent : FinanceEvent
{
    public abstract Instrument Instrument { get; }

    // Market data is normal priority content
    public override EventKind Kind => EventKind.Content;
    public override EventPriority Priority => EventPriority.Normal;
}

/// <summary>
/// Execution events (fills, rejects, cancellations).
/// Higher priority - processed before market data in live trading.
/// </summary>
public abstract record ExecutionEvent : FinanceEvent
{
    // Execution events are higher priority
    public override EventKind Kind => EventKind.Content;
    public override EventPriority Priority => EventPriority.Control;
}

/// <summary>
/// Control events (user cancellation, risk limits).
/// Highest priority - jump the queue immediately.
/// </summary>
public abstract record ControlEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Control;
    public override EventPriority Priority => EventPriority.Immediate;
}

/// <summary>
/// Lifecycle events (session start/end, market open/close).
/// </summary>
public abstract record LifecycleEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Lifecycle;
    public override EventPriority Priority => EventPriority.Normal;
}

/// <summary>
/// Diagnostic events (metrics, performance snapshots).
/// Lowest priority - processed when idle.
/// </summary>
public abstract record DiagnosticEvent : FinanceEvent
{
    public override EventKind Kind => EventKind.Diagnostic;
    public override EventPriority Priority => EventPriority.Background;
}
