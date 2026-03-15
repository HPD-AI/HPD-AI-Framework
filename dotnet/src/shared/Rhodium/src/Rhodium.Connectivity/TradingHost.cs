using HPD.Events;
using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Trading host - drives strategy execution with a connector.
/// Same host works for backtest and live (different connectors).
/// </summary>
public sealed class TradingHost : IDisposable
{
    private readonly IConnector _connector;
    private readonly IClock _clock;
    private readonly IEventCoordinator _coordinator;
    private readonly TradingEngine _engine;
    private EngineState _state;

    public TradingHost(
        IConnector connector,
        IClock clock,
        IEventCoordinator coordinator,
        TradingEngine engine)
    {
        _connector = connector;
        _clock = clock;
        _coordinator = coordinator;
        _engine = engine;
        _state = new EngineState(
            new WorldState(),
            engine.Tensors,
            Instant.FromDateTimeOffset(clock.UtcNow),
            Sequence.Zero);
    }

    /// <summary>
    /// Run strategy to completion (backtest) or until cancelled (live).
    /// </summary>
    public async Task RunAsync(StrategyBase strategy, CancellationToken ct = default)
    {
        // Initialize strategy
        strategy.Initialize(_engine);

        // Get subscriptions from strategy's instruments
        var subscriptions = GetSubscriptions();

        // Start connector in background
        var connectorTask = _connector.StartAsync(subscriptions, _coordinator, ct);

        // Main loop - pull events from coordinator
        while (!ct.IsCancellationRequested)
        {
            // Try to read from event coordinator (priority-ordered)
            if (_coordinator.TryRead(out var rawEvt))
            {
                // Process finance events
                if (rawEvt is FinanceEvent financeEvt)
                {
                    ProcessEvent(financeEvt, strategy);
                }
            }
            else if (!_connector.IsConnected)
            {
                // Connector finished (end of backtest data)
                break;
            }
            else
            {
                // No events available, yield to prevent busy-wait
                await Task.Yield();
            }
        }

        // Wait for connector to complete
        await connectorTask;
    }

    private void ProcessEvent(FinanceEvent evt, StrategyBase strategy)
    {
        // Apply state transition
        EngineLoop.Tick(ref _state, evt, _engine.BatchMap);

        // Run strategy tick
        strategy.OnTick();
    }

    private IEnumerable<Subscription> GetSubscriptions()
    {
        // Get instruments from BatchMap and subscribe to all market data
        // TODO: More sophisticated subscription management based on strategy needs
        var instruments = new List<Instrument>();

        // For now, return empty - strategies will need to register instruments
        // Real implementation would inspect BatchMap.GetInstruments()
        foreach (var instrument in instruments)
        {
            yield return new Subscription(instrument, SubscriptionType.Trades);
            yield return new Subscription(instrument, SubscriptionType.Quotes);
        }
    }

    public void Dispose()
    {
        _connector.Dispose();
    }
}
