using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Simulation.Frames;

namespace Rhodium.Simulation.Projection;

/// <summary>
/// Projects replayable semantic finance events into optional local struct frames.
/// </summary>
public sealed class SimulationStructFrameProjector
{
    private const decimal PriceScale = 1_000_000m;
    private const decimal QuantityScale = 1_000_000m;
    private const decimal MoneyScale = 1_000_000m;

    /// <summary>Project one semantic finance event into local struct-frame lanes when enabled.</summary>
    public void Apply(
        FinanceEvent evt,
        RhodiumRuntime runtime,
        SimulationFrameBus frames,
        SimulationFrameMode mode)
    {
        if (mode == SimulationFrameMode.Disabled)
            return;

        switch (evt)
        {
            case QuoteReceived quote when EmitsMarketData(mode):
                ApplyQuote(quote, runtime, frames);
                break;
            case TradeOccurred trade when EmitsMarketData(mode):
                ApplyTrade(trade, runtime, frames);
                break;
            case BookLevelDeltaReceived delta when EmitsMarketData(mode):
                ApplyLevelDelta(delta.Instrument, delta.Delta, delta.Time.Nanos, runtime, frames);
                break;
            case BookLevelDeltasReceived deltas when EmitsMarketData(mode):
                foreach (var delta in deltas.Deltas)
                    ApplyLevelDelta(deltas.Instrument, delta, deltas.Time.Nanos, runtime, frames);
                break;
            case BookDepthSnapshotReceived snapshot when EmitsMarketData(mode):
                ApplyDepth(snapshot.Instrument, snapshot.Bids, snapshot.Asks, snapshot.Depth, snapshot.VenueSequence, snapshot.Time.Nanos, runtime, frames);
                break;
            case BookDepth10Received snapshot when EmitsMarketData(mode):
                ApplyDepth(snapshot.Instrument, snapshot.Bids, snapshot.Asks, depth: 10, snapshot.VenueSequence, snapshot.Time.Nanos, runtime, frames);
                break;
            case BookOrderAdded added when EmitsMarketData(mode):
                ApplyBookOrderAdded(added, runtime, frames);
                break;
            case BookOrderModified modified when EmitsMarketData(mode):
                ApplyBookOrderModified(modified, runtime, frames);
                break;
            case BookOrderDeleted deleted when EmitsMarketData(mode):
                ApplyBookOrderDeleted(deleted, runtime, frames);
                break;
            case BookOrderExecuted executed when EmitsMarketData(mode):
                ApplyBookOrderExecuted(executed, runtime, frames);
                break;
            case OrderFilled fill when EmitsExecution(mode):
                ApplyFill(fill, runtime, frames);
                break;
        }
    }

    private static bool EmitsMarketData(SimulationFrameMode mode)
        => mode is SimulationFrameMode.MarketData or SimulationFrameMode.All;

    private static bool EmitsExecution(SimulationFrameMode mode)
        => mode is SimulationFrameMode.Execution or SimulationFrameMode.All;

    private static void ApplyQuote(QuoteReceived evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new QuoteFrame(
            instrumentIndex,
            ScalePrice(evt.Quote.Bid),
            ScalePrice(evt.Quote.Ask),
            ScaleQty(evt.Quote.BidSize),
            ScaleQty(evt.Quote.AskSize),
            evt.Quote.Time.ExchangeTime.Nanos));
    }

    private static void ApplyTrade(TradeOccurred evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new TradeFrame(
            instrumentIndex,
            ScalePrice(evt.Trade.Price),
            ScaleQty(evt.Trade.Size),
            evt.Trade.AggressorSide,
            evt.Trade.Time.ExchangeTime.Nanos));
    }

    private static void ApplyLevelDelta(
        Instrument instrument,
        BookLevelDelta delta,
        long timestampNs,
        RhodiumRuntime runtime,
        SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, instrument, out var instrumentIndex))
            return;

        frames.Emit(new BookLevelDeltaFrame(
            instrumentIndex,
            delta.Side,
            ScalePrice(delta.Price),
            ScaleQty(delta.Size),
            delta.Action,
            delta.VenueSequence,
            timestampNs));
    }

    private static void ApplyDepth(
        Instrument instrument,
        IReadOnlyList<Level> bids,
        IReadOnlyList<Level> asks,
        int depth,
        long venueSequence,
        long timestampNs,
        RhodiumRuntime runtime,
        SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, instrument, out var instrumentIndex))
            return;

        var bidCount = Math.Min(depth, bids.Count);
        for (var i = 0; i < bidCount; i++)
            EmitDepthLevel(frames, instrumentIndex, depth, i, Side.Buy, bids[i], venueSequence, timestampNs);

        var askCount = Math.Min(depth, asks.Count);
        for (var i = 0; i < askCount; i++)
            EmitDepthLevel(frames, instrumentIndex, depth, i, Side.Sell, asks[i], venueSequence, timestampNs);
    }

    private static void EmitDepthLevel(
        SimulationFrameBus frames,
        int instrumentIndex,
        int depth,
        int levelIndex,
        Side side,
        Level level,
        long venueSequence,
        long timestampNs)
        => frames.Emit(new BookDepthLevelFrame(
            instrumentIndex,
            depth,
            levelIndex,
            side,
            ScalePrice(level.Price),
            ScaleQty(level.Size),
            level.OrderCount,
            venueSequence,
            timestampNs));

    private static void ApplyBookOrderAdded(BookOrderAdded evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new BookOrderAddedFrame(
            instrumentIndex,
            evt.Order.OrderId.Value,
            evt.Order.Side,
            ScalePrice(evt.Order.Price),
            ScaleQty(evt.Order.Size),
            evt.VenueSequence,
            evt.Time.Nanos));
    }

    private static void ApplyBookOrderModified(BookOrderModified evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new BookOrderModifiedFrame(
            instrumentIndex,
            evt.Order.OrderId.Value,
            evt.Order.Side,
            ScalePrice(evt.Order.Price),
            ScaleQty(evt.Order.Size),
            evt.VenueSequence,
            evt.Time.Nanos));
    }

    private static void ApplyBookOrderDeleted(BookOrderDeleted evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new BookOrderDeletedFrame(
            instrumentIndex,
            evt.OrderId.Value,
            evt.VenueSequence,
            evt.Time.Nanos));
    }

    private static void ApplyBookOrderExecuted(BookOrderExecuted evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new BookOrderExecutedFrame(
            instrumentIndex,
            evt.OrderId.Value,
            ScaleQty(evt.ExecutedSize),
            evt.VenueSequence,
            evt.Time.Nanos));
    }

    private static void ApplyFill(OrderFilled evt, RhodiumRuntime runtime, SimulationFrameBus frames)
    {
        if (!TryGetInstrumentIndex(runtime, evt.Instrument, out var instrumentIndex))
            return;

        frames.Emit(new ExecutionFillFrame(
            evt.StrategyId.Value,
            evt.VariantId,
            instrumentIndex,
            evt.OrderId.Value,
            evt.VenueOrderId.Value,
            evt.ExecutionId.Value,
            evt.Side,
            ScalePrice(evt.FillPrice),
            ScaleQty(evt.FilledQty),
            ScaleMoney(evt.Commission),
            GetCurrencyId(evt.Commission.Currency),
            evt.Time.Nanos));
    }

    private static bool TryGetInstrumentIndex(RhodiumRuntime runtime, Instrument instrument, out int instrumentIndex)
    {
        try
        {
            var range = runtime.BatchMap.GetInstrumentRange(instrument);
            instrumentIndex = range.Start;
            return true;
        }
        catch (KeyNotFoundException)
        {
            instrumentIndex = 0;
            return false;
        }
    }

    private static long ScalePrice(Price price)
        => DecimalToInt64(price.Value * PriceScale);

    private static long ScaleQty(Qty qty)
        => DecimalToInt64(qty.Value * QuantityScale);

    private static long ScaleMoney(Money money)
        => DecimalToInt64(money.Amount * MoneyScale);

    private static int GetCurrencyId(Currency currency)
        => currency.Code is null ? 0 : StringComparer.Ordinal.GetHashCode(currency.Code);

    private static long DecimalToInt64(decimal value)
        => decimal.ToInt64(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
}
