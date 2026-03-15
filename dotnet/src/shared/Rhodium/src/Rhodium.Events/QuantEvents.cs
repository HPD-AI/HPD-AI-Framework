using HPD.Events;

namespace Rhodium.Events;

// ==================== QUANT EVENTS ====================

/// <summary>
/// Internal events produced by the Quant Fabric and re-entered into the Host/Runner pipeline.
/// These are framework-generated (not exchange facts) and are typically not persisted.
/// </summary>
public abstract record QuantEvent : FinanceEvent
{
    public new EventKind Kind => EventKind.Content;
    public new EventPriority Priority => EventPriority.Normal;
}

/// <summary>
/// Quant Fabric finished a computation for a specific request.
/// The Host/Runner MUST gate acceptance by (Sequence, BatchMap.Version).
/// </summary>
public sealed record QuantResultReady(
    Rhodium.Quant.QuantResult Result
) : QuantEvent;
