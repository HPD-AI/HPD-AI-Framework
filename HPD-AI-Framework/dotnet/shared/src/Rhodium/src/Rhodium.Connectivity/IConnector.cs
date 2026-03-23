using HPD.Events;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Universal connector interface - same API for backtest and live.
/// Connector PUSHES events into the coordinator, Host PULLS from it.
/// </summary>
public interface IConnector : IDisposable
{
    /// <summary>
    /// Exchange identifier (e.g., REPLAY, BINANCE, ALPACA).
    /// </summary>
    ExchangeId Exchange { get; }

    /// <summary>
    /// Rate limiter for outbound requests.
    /// </summary>
    IRateLimiter RateLimiter { get; }

    /// <summary>
    /// Whether the connector is currently connected and streaming.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Start the connector and stream market data.
    /// Events are emitted to the coordinator for priority-based routing.
    /// Returns when the stream ends (backtest) or is cancelled (live).
    /// </summary>
    /// <param name="subscriptions">Instruments and data types to subscribe to</param>
    /// <param name="coordinator">Event coordinator for priority-based event emission</param>
    /// <param name="ct">Cancellation token</param>
    Task StartAsync(
        IEnumerable<Subscription> subscriptions,
        IEventCoordinator coordinator,
        CancellationToken ct);

    /// <summary>
    /// Submit order to exchange (or simulation).
    /// Connector emits OrderAccepted/OrderRejected to coordinator.
    /// </summary>
    Task SubmitOrderAsync(SubmitOrder command, CancellationToken ct);

    /// <summary>
    /// Cancel order on exchange (or simulation).
    /// Connector emits OrderCancelled/OrderRejected to coordinator.
    /// </summary>
    Task CancelOrderAsync(CancelOrder command, CancellationToken ct);

    /// <summary>
    /// Modify order on exchange (or simulation).
    /// Connector emits OrderModified/OrderRejected to coordinator.
    /// </summary>
    Task ModifyOrderAsync(ModifyOrder command, CancellationToken ct);
}
