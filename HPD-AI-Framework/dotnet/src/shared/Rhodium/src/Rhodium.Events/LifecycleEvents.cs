using Rhodium.Primitives;

namespace Rhodium.Events;

// ==================== LIFECYCLE EVENTS ====================

/// <summary>
/// Scheduled event fired.
/// </summary>
public sealed record Scheduled(
    string Name
) : LifecycleEvent;

/// <summary>
/// Trading session started.
/// </summary>
public sealed record SessionStarted : LifecycleEvent;

/// <summary>
/// Trading session ended.
/// </summary>
public sealed record SessionEnded : LifecycleEvent;

/// <summary>
/// Market opened for trading.
/// </summary>
public sealed record MarketOpened(
    Venue Venue
) : LifecycleEvent;

/// <summary>
/// Market closed for trading.
/// </summary>
public sealed record MarketClosed(
    Venue Venue
) : LifecycleEvent;

/// <summary>
/// Pre-market session started.
/// </summary>
public sealed record PreMarketOpened(
    Venue Venue
) : LifecycleEvent;

/// <summary>
/// Post-market session started.
/// </summary>
public sealed record PostMarketOpened(
    Venue Venue
) : LifecycleEvent;

/// <summary>
/// Universe membership changed (instruments added/removed from tradeable set).
/// Universe changes extend IBatchMap and grow ITensorStore/PagedWorldState.
/// Any universe change MUST increment IBatchMap.Version to invalidate cached handles/decisions and reject stale quant results.
/// </summary>
public sealed record UniverseChanged(
    IReadOnlySet<Instrument> Added,
    IReadOnlySet<Instrument> Removed,
    string UniverseName
) : LifecycleEvent
{
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
