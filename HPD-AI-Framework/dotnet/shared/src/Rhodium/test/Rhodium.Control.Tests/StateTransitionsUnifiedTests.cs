using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control.Tests;

public class StateTransitionsUnifiedTests
{
    [Fact]
    public void Apply_BarClosed_WritesMarketFields()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        runtime.BatchMap.AddInstrument(inst);
        runtime.Tensors.Grow();

        var bar = new Bar(
            new Price(100m),
            new Price(101m),
            new Price(99m),
            new Price(100.5m),
            new Qty(1000m),
            Instant.Now,
            Duration.FromMinutes(1));

        StateTransitions.Apply(runtime.WorldState, runtime.Tensors, runtime.BatchMap, new BarClosed(inst, bar));

        Assert.Equal(100.5, runtime.Tensors.GetScalar(Field.CloseRaw, 0).Value);
        Assert.Equal(1000, runtime.Tensors.GetScalar(Field.VolumeRaw, 0).Value);
    }

    [Fact]
    public void Apply_BarClosed_AdjustmentKernelCopiesRawToAdjustedWhenScalesAreUnset()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        runtime.BatchMap.AddInstrument(inst);
        runtime.Tensors.Grow();

        var bar = new Bar(
            new Price(100m),
            new Price(101m),
            new Price(99m),
            new Price(100.5m),
            new Qty(1000m),
            Instant.Now,
            Duration.FromMinutes(1));

        StateTransitions.Apply(runtime.WorldState, runtime.Tensors, runtime.BatchMap, new BarClosed(inst, bar));
        runtime.CreateMarketKernel().RunAdjustmentKernel();

        Assert.Equal(100m, (decimal)runtime.Tensors.GetScalar(Field.Open, 0).Value);
        Assert.Equal(101m, (decimal)runtime.Tensors.GetScalar(Field.High, 0).Value);
        Assert.Equal(99m, (decimal)runtime.Tensors.GetScalar(Field.Low, 0).Value);
        Assert.Equal(100.5m, (decimal)runtime.Tensors.GetScalar(Field.Close, 0).Value);
        Assert.Equal(1000m, (decimal)runtime.Tensors.GetScalar(Field.Volume, 0).Value);
    }

    [Fact]
    public void Apply_TradeOccurred_WritesLastTradeFields()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        runtime.BatchMap.AddInstrument(inst, variantCount: 2);
        runtime.Tensors.Grow();

        var trade = new Trade(
            new Price(100.25m, Currency.USD),
            new Qty(37m),
            Side.Buy,
            DualTimestamp.Synchronized(Instant.Now));

        StateTransitions.Apply(runtime.WorldState, runtime.Tensors, runtime.BatchMap, new TradeOccurred(inst, trade));
        runtime.CreateMarketKernel().RunAdjustmentKernel();

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(100.25m, (decimal)runtime.Tensors.GetScalar(Field.Close, i).Value);
            Assert.Equal(37m, (decimal)runtime.Tensors.GetScalar(Field.Volume, i).Value);
        }
    }

    [Fact]
    public void Apply_BookUpdated_WritesTopOfBookFields()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        runtime.BatchMap.AddInstrument(inst, variantCount: 2);
        runtime.Tensors.Grow();

        var book = new Book
        {
            Instrument = inst,
            Time = Instant.Now,
            Bids =
            [
                new Level(new Price(100m, Currency.USD), new Qty(20m)),
                new Level(new Price(99.99m, Currency.USD), new Qty(30m))
            ],
            Asks =
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(10m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(40m))
            ]
        };

        StateTransitions.Apply(runtime.WorldState, runtime.Tensors, runtime.BatchMap, new BookUpdated(inst, book));
        runtime.CreateMarketKernel().RunAdjustmentKernel();

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(100m, (decimal)runtime.Tensors.GetScalar(Field.Bid, i).Value);
            Assert.Equal(100.01m, (decimal)runtime.Tensors.GetScalar(Field.Ask, i).Value);
            Assert.Equal(20m, (decimal)runtime.Tensors.GetScalar(Field.BidSize, i).Value);
            Assert.Equal(10m, (decimal)runtime.Tensors.GetScalar(Field.AskSize, i).Value);
        }
    }

    [Fact]
    public void Apply_OrderFilled_RoutesToSubmittingStrategy()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        runtime.BatchMap.AddInstrument(inst);
        var strategyA = new StrategyId(1);
        var strategyB = new StrategyId(2);

        var fill = new OrderFilled(
            OrderId.New(),
            inst,
            0,
            strategyA,
            Side.Buy,
            new Qty(50m),
            new Price(100m),
            Money.Zero(Currency.USD));

        StateTransitions.Apply(runtime.WorldState, runtime.Tensors, runtime.BatchMap, fill);

        Assert.Equal(50m, runtime.WorldState.PositionAt(strategyA, 0).Quantity);
        Assert.Equal(0m, runtime.WorldState.PositionAt(strategyB, 0).Quantity);
    }
}
