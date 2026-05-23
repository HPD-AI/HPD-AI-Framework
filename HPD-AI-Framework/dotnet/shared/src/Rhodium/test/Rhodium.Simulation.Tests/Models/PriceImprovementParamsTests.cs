using Rhodium.Primitives;
using Rhodium.Simulation;

namespace Rhodium.Simulation.Tests.Models;

public class PriceImprovementParamsTests
{
    [Fact]
    public void Apply_NoneReturnsOriginalPrice()
    {
        var price = new Price(100m, Currency.USD);

        var improved = PriceImprovementParams.None.Apply(price, Side.Buy, isMaker: false);

        Assert.Equal(price, improved);
    }

    [Fact]
    public void Apply_FixedBpsImprovesBuyPrice()
    {
        var price = new Price(100m, Currency.USD);
        var improvement = PriceImprovementParams.FixedBps(takerBps: 10m);

        var improved = improvement.Apply(price, Side.Buy, isMaker: false);

        Assert.Equal(new Price(99.90m, Currency.USD), improved);
    }

    [Fact]
    public void Apply_FixedBpsImprovesSellPrice()
    {
        var price = new Price(100m, Currency.USD);
        var improvement = PriceImprovementParams.FixedBps(takerBps: 10m);

        var improved = improvement.Apply(price, Side.Sell, isMaker: false);

        Assert.Equal(new Price(100.10m, Currency.USD), improved);
    }

    [Fact]
    public void Apply_UsesMakerBpsForMakerFills()
    {
        var price = new Price(100m, Currency.USD);
        var improvement = PriceImprovementParams.FixedBps(takerBps: 0m, makerBps: 5m);

        var improved = improvement.Apply(price, Side.Buy, isMaker: true);

        Assert.Equal(new Price(99.95m, Currency.USD), improved);
    }

    [Fact]
    public void FixedBps_RejectsNegativeInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceImprovementParams.FixedBps(-1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceImprovementParams.FixedBps(0m, -1m));
    }
}
