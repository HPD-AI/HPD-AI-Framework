using Rhodium.Events;
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
    public static void Apply(WorldState state, ITensorStore tensors, IBatchMap map, FinanceEvent @event)
    {
        switch (@event)
        {
            case BarClosed e:
                ApplyBarClosed(tensors, map, e);
                break;

            case OrderFilled e:
                ApplyOrderFilled(state, map, e);
                break;

            // Add other event handlers as needed
            default:
                // Unhandled events are ignored
                break;
        }
    }

    private static void ApplyBarClosed(ITensorStore tensors, IBatchMap map, BarClosed e)
    {
        var (start, length) = map.GetInstrumentRange(e.Instrument);

        // Broadcast bar data to all variants of this instrument
        tensors.Broadcast(Field.OpenRaw, new PriceF64((double)e.Bar.Open.Value), start, length);
        tensors.Broadcast(Field.HighRaw, new PriceF64((double)e.Bar.High.Value), start, length);
        tensors.Broadcast(Field.LowRaw, new PriceF64((double)e.Bar.Low.Value), start, length);
        tensors.Broadcast(Field.CloseRaw, new PriceF64((double)e.Bar.Close.Value), start, length);
        tensors.Broadcast(Field.VolumeRaw, new SizeF64((double)e.Bar.Volume.Value), start, length);
    }

    private static void ApplyOrderFilled(WorldState state, IBatchMap map, OrderFilled e)
    {
        var (start, _) = map.GetInstrumentRange(e.Instrument);
        var virtualIndex = start + e.VariantId; // VariantId is offset within instrument range

        ref var pos = ref state.PositionAt(virtualIndex);
        pos.ApplyFill(e.Side, e.FilledQty, e.FillPrice, e.Commission);
    }
}
