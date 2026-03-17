using Rhodium.Control;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control.Tests;

public class WorldStateTests
{
    [Fact]
    public void WorldState_AllocatePage_CreatesPositionsAndOrders()
    {
        var state = new WorldState();
        var map = new TestBatchMap();

        state.AllocatePage(0, map);

        // Should be able to access positions and orders
        ref var pos = ref state.PositionAt(0);
        ref var ord = ref state.OrderAt(0);

        Assert.NotNull(pos);
        Assert.NotNull(ord);
        Assert.True(pos.IsFlat);
    }

    [Fact]
    public void WorldState_PositionAt_ReturnsCorrectReference()
    {
        var state = new WorldState();
        var map = new TestBatchMap();
        state.AllocatePage(0, map);

        ref var pos = ref state.PositionAt(10);

        // Modify position via ApplyFill
        pos.ApplyFill(Side.Buy, new Qty(100m), new Price(50m), Money.Zero(Currency.USD));

        // Get reference again and verify it's the same
        ref var pos2 = ref state.PositionAt(10);
        Assert.Equal(100m, pos2.Quantity.Value);
    }

    [Fact]
    public void WorldState_MultiplePages_HandlesCorrectly()
    {
        var state = new WorldState();
        var map = new TestBatchMap();

        state.AllocatePage(0, map);
        state.AllocatePage(1, map);

        // Access from both pages
        ref var pos0 = ref state.PositionAt(10);    // Page 0
        ref var pos1 = ref state.PositionAt(1500);  // Page 1

        pos0.ApplyFill(Side.Buy, new Qty(50m), new Price(100m), Money.Zero(Currency.USD));
        pos1.ApplyFill(Side.Buy, new Qty(100m), new Price(100m), Money.Zero(Currency.USD));

        Assert.Equal(50m, state.PositionAt(10).Quantity.Value);
        Assert.Equal(100m, state.PositionAt(1500).Quantity.Value);
    }

    [Fact]
    public void WorldState_AllocatePage_IsIdempotent()
    {
        var state = new WorldState();
        var map = new TestBatchMap();

        state.AllocatePage(0, map);
        state.AllocatePage(0, map); // Should not throw

        ref var pos = ref state.PositionAt(0);
        Assert.NotNull(pos);
    }
}

// Test implementation of IBatchMap
internal class TestBatchMap : IBatchMap
{
    public int Version => 1;
    public int TotalSize => 2048;
    public TensorBasis CurrentBasis => new(10, 10);

    public (int Start, int Length) GetInstrumentRange(Instrument instrument) => (0, 100);

    public (Instrument Inst, int VariantId) GetContext(int virtualIndex) =>
        (new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NYSE), 0);

    public (Instrument Inst, int VariantId) SafeGetContext(int virtualIndex)
    {
        if (virtualIndex >= TotalSize)
            return (Instrument.Unknown, 0);
        return GetContext(virtualIndex);
    }
}
