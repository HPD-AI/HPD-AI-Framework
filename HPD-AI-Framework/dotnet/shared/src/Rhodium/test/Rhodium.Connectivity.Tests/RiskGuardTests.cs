using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class DefaultRiskGuardTests
{
    private static SubmitOrder CreateOrder(
        decimal qty = 100m,
        decimal? limitPrice = 100m,
        Side side = Side.Buy,
        OrderType type = OrderType.Limit)
    {
        var instrument = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        return new SubmitOrder
        {
            OrderId = OrderId.New(),
            Instrument = instrument,
            VariantId = 0,
            Side = side,
            Quantity = new Qty(qty),
            Type = type,
            LimitPrice = limitPrice.HasValue ? new Price(limitPrice.Value, Currency.USD) : null
        };
    }

    [Fact]
    public void Check_ApprovedForValidOrder()
    {
        var guard = new DefaultRiskGuard();
        var order = CreateOrder(qty: 100m, limitPrice: 100m);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 0m);

        Assert.True(result.IsApproved);
        Assert.Null(result.Reason);
        Assert.Equal(RiskCode.None, result.Code);
    }

    [Fact]
    public void Check_RefusedWhenOrderSizeExceedsMax()
    {
        var guard = new DefaultRiskGuard { MaxOrderSize = 1000m };
        var order = CreateOrder(qty: 2000m);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 0m);

        Assert.False(result.IsApproved);
        Assert.Equal(RiskCode.MaxSize, result.Code);
        Assert.Contains("Order size", result.Reason);
    }

    [Fact]
    public void Check_RefusedWhenPositionExceedsMax_Buy()
    {
        var guard = new DefaultRiskGuard { MaxPositionSize = 1000m };
        var order = CreateOrder(qty: 500m, side: Side.Buy);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 600m);

        Assert.False(result.IsApproved);
        Assert.Equal(RiskCode.MaxPosition, result.Code);
        Assert.Contains("position", result.Reason);
    }

    [Fact]
    public void Check_RefusedWhenPositionExceedsMax_Sell()
    {
        var guard = new DefaultRiskGuard { MaxPositionSize = 1000m };
        var order = CreateOrder(qty: 500m, side: Side.Sell);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: -600m);

        Assert.False(result.IsApproved);
        Assert.Equal(RiskCode.MaxPosition, result.Code);
    }

    [Fact]
    public void Check_RefusedWhenNotionalExceedsMax()
    {
        var guard = new DefaultRiskGuard { MaxNotional = new Money(10_000m, Currency.USD) };
        var order = CreateOrder(qty: 1000m, limitPrice: 20m); // Notional = 20,000

        var result = guard.Check(order, new Price(20m, Currency.USD), currentPosition: 0m);

        Assert.False(result.IsApproved);
        Assert.Equal(RiskCode.MaxNotional, result.Code);
        Assert.Contains("Notional", result.Reason);
    }

    [Fact]
    public void Check_RefusedWhenPriceDeviationExceedsMax()
    {
        var guard = new DefaultRiskGuard { MaxPriceDeviationPercent = 0.05m }; // 5%
        var order = CreateOrder(qty: 100m, limitPrice: 120m); // 20% above market

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 0m);

        Assert.False(result.IsApproved);
        Assert.Equal(RiskCode.PriceBand, result.Code);
        Assert.Contains("deviation", result.Reason);
    }

    [Fact]
    public void Check_ApprovedWhenPriceDeviationWithinLimit()
    {
        var guard = new DefaultRiskGuard { MaxPriceDeviationPercent = 0.10m }; // 10%
        var order = CreateOrder(qty: 100m, limitPrice: 105m); // 5% above market

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 0m);

        Assert.True(result.IsApproved);
    }

    [Fact]
    public void Check_SkipsPriceDeviationCheckWhenNoCurrentPrice()
    {
        var guard = new DefaultRiskGuard { MaxPriceDeviationPercent = 0.01m }; // Very strict
        var order = CreateOrder(qty: 100m, limitPrice: 200m);

        var result = guard.Check(order, currentPrice: null, currentPosition: 0m);

        Assert.True(result.IsApproved); // No price deviation check without market price
    }

    [Fact]
    public void Check_SkipsPriceDeviationCheckWhenNoLimitPrice()
    {
        var guard = new DefaultRiskGuard { MaxPriceDeviationPercent = 0.01m };
        var order = CreateOrder(qty: 100m, limitPrice: null);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 0m);

        Assert.True(result.IsApproved); // Market orders skip price deviation
    }

    [Fact]
    public void Check_UsesLimitPriceForNotionalWhenAvailable()
    {
        // Disable price deviation check by setting a high threshold
        var guard = new DefaultRiskGuard
        {
            MaxNotional = new Money(15_000m, Currency.USD),
            MaxPriceDeviationPercent = 1.0m // 100% to disable this check
        };
        // Limit price 100, market price 200
        // Should use limit price (100 * 100 = 10,000 < 15,000)
        var order = CreateOrder(qty: 100m, limitPrice: 100m);

        var result = guard.Check(order, new Price(200m, Currency.USD), currentPosition: 0m);

        Assert.True(result.IsApproved);
    }

    [Fact]
    public void Check_UsesCurrentPriceForNotionalWhenNoLimitPrice()
    {
        var guard = new DefaultRiskGuard { MaxNotional = new Money(5_000m, Currency.USD) };
        // No limit price, market price 100
        // Should use market price (100 * 100 = 10,000 > 5,000)
        var order = CreateOrder(qty: 100m, limitPrice: null);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 0m);

        Assert.False(result.IsApproved);
        Assert.Equal(RiskCode.MaxNotional, result.Code);
    }

    [Fact]
    public void Check_PositionLimitAllowsReducingPosition()
    {
        var guard = new DefaultRiskGuard { MaxPositionSize = 1000m };
        // Currently long 900, selling 500 brings us to 400 (within limit)
        var order = CreateOrder(qty: 500m, side: Side.Sell);

        var result = guard.Check(order, new Price(100m, Currency.USD), currentPosition: 900m);

        Assert.True(result.IsApproved);
    }

    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var guard = new DefaultRiskGuard();

        Assert.Equal(1_000_000m, guard.MaxNotional.Amount);
        Assert.Equal(Currency.USD, guard.MaxNotional.Currency);
        Assert.Equal(0.10m, guard.MaxPriceDeviationPercent);
        Assert.Equal(10_000m, guard.MaxOrderSize);
        Assert.Equal(100_000m, guard.MaxPositionSize);
    }
}

public class RiskDecisionTests
{
    [Fact]
    public void Approved_CreatesApprovedDecision()
    {
        var decision = RiskDecision.Approved();

        Assert.True(decision.IsApproved);
        Assert.Null(decision.Reason);
        Assert.Equal(RiskCode.None, decision.Code);
    }

    [Fact]
    public void Refused_CreatesRefusedDecision()
    {
        var decision = RiskDecision.Refused("Test reason", RiskCode.MaxSize);

        Assert.False(decision.IsApproved);
        Assert.Equal("Test reason", decision.Reason);
        Assert.Equal(RiskCode.MaxSize, decision.Code);
    }
}
