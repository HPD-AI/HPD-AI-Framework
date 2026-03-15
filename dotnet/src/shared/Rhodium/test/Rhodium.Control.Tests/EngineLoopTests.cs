using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control.Tests;

public class EngineLoopTests
{
    [Fact]
    public void EngineLoop_Tick_UpdatesTimeAndSequence()
    {
        var world = new WorldState();
        var tensors = new PagedTensorStore();
        var map = new TestBatchMap();
        var time = Instant.Now;
        var seq = new Sequence(1);

        // Grow tensor store
        tensors.Grow();

        var state = new EngineState(world, tensors, time, seq);

        var bar = new Bar(
            new Price(100m),
            new Price(100m),
            new Price(100m),
            new Price(100m),
            Qty.Zero,
            Instant.Now,
            Duration.FromMinutes(1)
        );
        var evt = new BarClosed(Instrument.Unknown, bar);

        EngineLoop.Tick(ref state, evt, map);

        // Sequence should increment
        Assert.Equal(2UL, state.Sequence.Value);

        // Time should update to event time
        Assert.Equal(evt.Time, state.Time);
    }

    [Fact]
    public void EngineLoop_Tick_AppliesStateTransitions()
    {
        var world = new WorldState();
        var tensors = new PagedTensorStore();
        var map = new TestBatchMap();
        world.AllocatePage(0, map);

        var state = new EngineState(world, tensors, Instant.Now, new Sequence(0));

        var evt = new OrderFilled(
            OrderId.New(),
            new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE),
            0,
            Side.Buy,
            new Qty(50m),
            new Price(100m),
            Money.Zero(Currency.USD)
        );

        EngineLoop.Tick(ref state, evt, map);

        // Position should be updated
        ref var pos = ref world.PositionAt(0);
        Assert.Equal(50m, pos.Quantity.Value);
    }
}
