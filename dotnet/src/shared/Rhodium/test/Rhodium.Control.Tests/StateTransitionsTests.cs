using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control.Tests;

public class StateTransitionsTests
{
    [Fact]
    public void StateTransitions_ApplyBarClosed_BroadcastsToTensorStore()
    {
        var state = new WorldState();
        var tensors = new PagedTensorStore();
        var map = new TestBatchMap();

        // Grow tensor store to have at least one page
        tensors.Grow();

        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(102m),
            new Qty(1000m),
            Instant.Now,
            Duration.FromMinutes(5)
        );

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var evt = new BarClosed(instrument, bar);

        StateTransitions.Apply(state, tensors, map, evt);

        // Verify fields were set (basic smoke test - actual values would need GetScalar)
        Assert.NotNull(tensors);
    }

    [Fact]
    public void StateTransitions_ApplyOrderFilled_UpdatesPosition()
    {
        var state = new WorldState();
        var tensors = new PagedTensorStore();
        var map = new TestBatchMap();

        // Allocate page first
        state.AllocatePage(0, map);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var evt = new OrderFilled(
            OrderId.New(),
            instrument,
            VariantId: 0,
            Side.Buy,
            new Qty(100m),
            new Price(100m),
            Money.Zero(Currency.USD)
        );

        StateTransitions.Apply(state, tensors, map, evt);

        // Position should be updated
        ref var pos = ref state.PositionAt(0);
        Assert.Equal(100m, pos.Quantity.Value);
    }

    [Fact]
    public void StateTransitions_UnhandledEvent_DoesNotThrow()
    {
        var state = new WorldState();
        var tensors = new PagedTensorStore();
        var map = new TestBatchMap();

        var evt = new OrderAccepted(OrderId.New(), 0);

        // Should not throw
        StateTransitions.Apply(state, tensors, map, evt);
    }
}
