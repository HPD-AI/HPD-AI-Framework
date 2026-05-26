using Rhodium.Events;
using Rhodium.Control;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Simulation.Projection;

/// <summary>
/// Projects observable market state into Rhodium's tensor and depth runtime.
/// </summary>
public sealed class SimulationMarketProjector
{
    /// <summary>Apply one observable finance event to the strategy-facing runtime state.</summary>
    public StateTransitionResult Apply(FinanceEvent evt, RhodiumRuntime runtime)
    {
        switch (evt)
        {
            case BarClosed bar:
                ApplyBar(runtime, bar);
                return new StateTransitionResult { RequiresAdjustment = true };
            case QuoteReceived quote:
                ApplyQuote(runtime, quote);
                return new StateTransitionResult { RequiresAdjustment = true };
            case TradeOccurred trade:
                ApplyTrade(runtime, trade);
                return new StateTransitionResult { RequiresAdjustment = true };
            case BookSnapshotReceived book:
                ApplyBook(runtime, book);
                return new StateTransitionResult { RequiresAdjustment = true };
            case BookLevelDeltaReceived delta:
                ApplyDelta(runtime, delta.Instrument, delta.Delta, delta.Time);
                return StateTransitionResult.None;
            case BookLevelDeltasReceived deltas:
                foreach (var delta in deltas.Deltas)
                    ApplyDelta(runtime, deltas.Instrument, delta, deltas.Time);
                return StateTransitionResult.None;
            case BookDepthSnapshotReceived snapshot:
                ApplyDepthSnapshot(runtime, snapshot);
                return StateTransitionResult.None;
            case BookDepth10Received snapshot:
                ApplyDepth10(runtime, snapshot);
                return StateTransitionResult.None;
            case BookOrderAdded added:
                runtime.AddBookOrder(added.Instrument, added.Order, added.Time);
                return StateTransitionResult.None;
            case BookOrderModified modified:
                runtime.ModifyBookOrder(modified.Instrument, modified.Order, modified.Time);
                return StateTransitionResult.None;
            case BookOrderDeleted deleted:
                runtime.DeleteBookOrder(deleted.Instrument, deleted.OrderId);
                return StateTransitionResult.None;
            case BookOrderExecuted executed:
                runtime.ExecuteBookOrder(executed.Instrument, executed.OrderId, executed.ExecutedSize);
                return StateTransitionResult.None;
            default:
                return StateTransitionResult.None;
        }
    }

    private static void ApplyBar(RhodiumRuntime runtime, BarClosed e)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(e.Instrument);
        runtime.Tensors.Broadcast(Field.OpenRaw, new PriceF64((double)e.Bar.Open.Value), start, length);
        runtime.Tensors.Broadcast(Field.HighRaw, new PriceF64((double)e.Bar.High.Value), start, length);
        runtime.Tensors.Broadcast(Field.LowRaw, new PriceF64((double)e.Bar.Low.Value), start, length);
        runtime.Tensors.Broadcast(Field.CloseRaw, new PriceF64((double)e.Bar.Close.Value), start, length);
        runtime.Tensors.Broadcast(Field.VolumeRaw, new SizeF64((double)e.Bar.Volume.Value), start, length);
    }

    private static void ApplyQuote(RhodiumRuntime runtime, QuoteReceived e)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(e.Instrument);
        runtime.Tensors.Broadcast(Field.BidRaw, new PriceF64((double)e.Quote.Bid.Value), start, length);
        runtime.Tensors.Broadcast(Field.AskRaw, new PriceF64((double)e.Quote.Ask.Value), start, length);
        runtime.Tensors.Broadcast(Field.BidSizeRaw, new SizeF64((double)e.Quote.BidSize.Value), start, length);
        runtime.Tensors.Broadcast(Field.AskSizeRaw, new SizeF64((double)e.Quote.AskSize.Value), start, length);

        for (var i = 0; i < length; i++)
        {
            var virtualIndex = start + i;
            runtime.UpdateDepthLevel(virtualIndex, e.Instrument, Side.Buy, e.Quote.Bid, e.Quote.BidSize, e.Quote.Time.ExchangeTime);
            runtime.UpdateDepthLevel(virtualIndex, e.Instrument, Side.Sell, e.Quote.Ask, e.Quote.AskSize, e.Quote.Time.ExchangeTime);
        }
    }

    private static void ApplyTrade(RhodiumRuntime runtime, TradeOccurred e)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(e.Instrument);
        runtime.Tensors.Broadcast(Field.CloseRaw, new PriceF64((double)e.Trade.Price.Value), start, length);
        runtime.Tensors.Broadcast(Field.VolumeRaw, new SizeF64((double)e.Trade.Size.Value), start, length);
    }

    private static void ApplyBook(RhodiumRuntime runtime, BookSnapshotReceived e)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(e.Instrument);
        var bid = e.Book.BestBid;
        var ask = e.Book.BestAsk;

        runtime.Tensors.Broadcast(Field.BidRaw, new PriceF64((double)(bid?.Price.Value ?? 0m)), start, length);
        runtime.Tensors.Broadcast(Field.AskRaw, new PriceF64((double)(ask?.Price.Value ?? 0m)), start, length);
        runtime.Tensors.Broadcast(Field.BidSizeRaw, new SizeF64((double)(bid?.Size.Value ?? 0m)), start, length);
        runtime.Tensors.Broadcast(Field.AskSizeRaw, new SizeF64((double)(ask?.Size.Value ?? 0m)), start, length);

        for (var i = 0; i < length; i++)
        {
            var virtualIndex = start + i;
            runtime.ClearDepth(virtualIndex, e.Instrument);
            foreach (var level in e.Book.Bids)
                runtime.UpdateDepthLevel(virtualIndex, e.Instrument, Side.Buy, level.Price, level.Size, e.Book.Time);
            foreach (var level in e.Book.Asks)
                runtime.UpdateDepthLevel(virtualIndex, e.Instrument, Side.Sell, level.Price, level.Size, e.Book.Time);
        }
    }

    private static void ApplyDelta(RhodiumRuntime runtime, Instrument instrument, BookLevelDelta delta, Instant time)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(instrument);
        for (var i = 0; i < length; i++)
        {
            var virtualIndex = start + i;
            if (delta.Action == BookAction.Clear)
            {
                runtime.ClearDepth(virtualIndex, instrument);
                continue;
            }

            var quantity = delta.Action == BookAction.Delete ? Qty.Zero : delta.Size;
            runtime.UpdateDepthLevel(virtualIndex, instrument, delta.Side, delta.Price, quantity, time);
        }
    }

    private static void ApplyDepthSnapshot(RhodiumRuntime runtime, BookDepthSnapshotReceived e)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(e.Instrument);
        for (var i = 0; i < length; i++)
        {
            var virtualIndex = start + i;
            runtime.ClearDepth(virtualIndex, e.Instrument);
            ApplyLevels(runtime, virtualIndex, e.Instrument, Side.Buy, e.Bids, e.Depth, e.Time);
            ApplyLevels(runtime, virtualIndex, e.Instrument, Side.Sell, e.Asks, e.Depth, e.Time);
        }
    }

    private static void ApplyDepth10(RhodiumRuntime runtime, BookDepth10Received e)
    {
        var (start, length) = runtime.BatchMap.GetInstrumentRange(e.Instrument);
        for (var i = 0; i < length; i++)
        {
            var virtualIndex = start + i;
            runtime.ClearDepth(virtualIndex, e.Instrument);
            ApplyLevels(runtime, virtualIndex, e.Instrument, Side.Buy, e.Bids, 10, e.Time);
            ApplyLevels(runtime, virtualIndex, e.Instrument, Side.Sell, e.Asks, 10, e.Time);
        }
    }

    private static void ApplyLevels(
        RhodiumRuntime runtime,
        int virtualIndex,
        Instrument instrument,
        Side side,
        IReadOnlyList<Level> levels,
        int depth,
        Instant time)
    {
        var count = levels.Count < depth ? levels.Count : depth;
        for (var i = 0; i < count; i++)
        {
            var level = levels[i];
            runtime.UpdateDepthLevel(virtualIndex, instrument, side, level.Price, level.Size, time);
        }
    }
}
