using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control;

/// <summary>
/// State transitions operate on virtual indices.
/// Market data updates are written to *Raw tensor fields.
/// Fills update paged WorldState.
/// </summary>
public static class StateTransitions
{
    /// <summary>
    /// Apply an event to state (mutates paged arrays + tensor fields).
    /// Events are normalized to include VariantId (or virtual index) before this stage.
    /// </summary>
    public static StateTransitionResult Apply(WorldState state, ITensorStore tensors, IBatchMap map, FinanceEvent @event)
    {
        return @event switch
        {
            BarClosed e => ApplyBarClosed(tensors, map, e),
            QuoteReceived e => ApplyQuoteReceived(tensors, map, e),
            TradeOccurred e => ApplyTradeOccurred(tensors, map, e),
            BookUpdated e => ApplyBookUpdated(tensors, map, e),
            OrderFilled e => ApplyOrderFilled(state, map, e),
            _ => StateTransitionResult.None
        };
    }

    private static StateTransitionResult ApplyBarClosed(ITensorStore tensors, IBatchMap map, BarClosed e)
    {
        var (start, length) = map.GetInstrumentRange(e.Instrument);

        // Broadcast bar data to all variants of this instrument
        tensors.Broadcast(Field.OpenRaw, new PriceF64((double)e.Bar.Open.Value), start, length);
        tensors.Broadcast(Field.HighRaw, new PriceF64((double)e.Bar.High.Value), start, length);
        tensors.Broadcast(Field.LowRaw, new PriceF64((double)e.Bar.Low.Value), start, length);
        tensors.Broadcast(Field.CloseRaw, new PriceF64((double)e.Bar.Close.Value), start, length);
        tensors.Broadcast(Field.VolumeRaw, new SizeF64((double)e.Bar.Volume.Value), start, length);

        return new StateTransitionResult { RequiresAdjustment = true };
    }

    private static StateTransitionResult ApplyQuoteReceived(ITensorStore tensors, IBatchMap map, QuoteReceived e)
    {
        var (start, length) = map.GetInstrumentRange(e.Instrument);

        tensors.Broadcast(Field.BidRaw, new PriceF64((double)e.Quote.Bid.Value), start, length);
        tensors.Broadcast(Field.AskRaw, new PriceF64((double)e.Quote.Ask.Value), start, length);
        tensors.Broadcast(Field.BidSizeRaw, new SizeF64((double)e.Quote.BidSize.Value), start, length);
        tensors.Broadcast(Field.AskSizeRaw, new SizeF64((double)e.Quote.AskSize.Value), start, length);

        return new StateTransitionResult { RequiresAdjustment = true };
    }

    private static StateTransitionResult ApplyTradeOccurred(ITensorStore tensors, IBatchMap map, TradeOccurred e)
    {
        var (start, length) = map.GetInstrumentRange(e.Instrument);

        tensors.Broadcast(Field.CloseRaw, new PriceF64((double)e.Trade.Price.Value), start, length);
        tensors.Broadcast(Field.VolumeRaw, new SizeF64((double)e.Trade.Size.Value), start, length);

        return new StateTransitionResult { RequiresAdjustment = true };
    }

    private static StateTransitionResult ApplyBookUpdated(ITensorStore tensors, IBatchMap map, BookUpdated e)
    {
        var (start, length) = map.GetInstrumentRange(e.Instrument);

        var bid = e.Book.BestBid;
        var ask = e.Book.BestAsk;

        tensors.Broadcast(Field.BidRaw, new PriceF64((double)(bid?.Price.Value ?? 0m)), start, length);
        tensors.Broadcast(Field.AskRaw, new PriceF64((double)(ask?.Price.Value ?? 0m)), start, length);
        tensors.Broadcast(Field.BidSizeRaw, new SizeF64((double)(bid?.Size.Value ?? 0m)), start, length);
        tensors.Broadcast(Field.AskSizeRaw, new SizeF64((double)(ask?.Size.Value ?? 0m)), start, length);

        return new StateTransitionResult { RequiresAdjustment = true };
    }

    private static StateTransitionResult ApplyOrderFilled(WorldState state, IBatchMap map, OrderFilled e)
    {
        var (start, _) = map.GetInstrumentRange(e.Instrument);
        var virtualIndex = start + e.VariantId; // VariantId is offset within instrument range
        var assetId = new AssetId(virtualIndex);

        ref var pos = ref state.PositionAt(e.StrategyId, virtualIndex);
        var previous = pos;
        pos.ApplyFill(e.Side, e.FilledQty, e.FillPrice, e.Commission);
        var current = pos;

        return new StateTransitionResult
        {
            PositionTransition = new PositionTransition
            {
                StrategyId = e.StrategyId,
                AssetId = assetId,
                Kind = ClassifyPositionTransition(previous, current),
                Previous = previous,
                Current = current
            }
        };
    }

    private static PositionTransitionKind ClassifyPositionTransition(PositionState previous, PositionState current)
    {
        if (previous.IsFlat && !current.IsFlat)
            return PositionTransitionKind.Opened;
        if (!previous.IsFlat && current.IsFlat)
            return PositionTransitionKind.Closed;
        if (!previous.IsFlat && !current.IsFlat)
            return PositionTransitionKind.Changed;

        return PositionTransitionKind.None;
    }
}
