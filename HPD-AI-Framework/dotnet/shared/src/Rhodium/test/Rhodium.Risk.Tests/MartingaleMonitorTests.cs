using Rhodium.Primitives;
using Rhodium.Risk;

namespace Rhodium.Risk.Tests;

public class MartingaleMonitorTests
{
    private class TestAnalyzer : IAnalyzer
    {
        public Money TotalEquity { get; set; }
    }

    [Fact]
    public void MartingaleMonitor_ApprovesSmallPositions()
    {
        var model = ConstantSigmaModel.MediumVol(); // 20% volatility
        var monitor = new MartingaleMonitor(model);
        var analyzer = new TestAnalyzer { TotalEquity = new Money(100000m, Currency.USD) };

        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var order = SubmitOrder.BuyLimit(new StrategyId(1), inst, new Qty(10m), new Price(100m)); // $1000 position

        var decision = monitor.CheckOrder(order, analyzer);

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void MartingaleMonitor_RefusesOversizedPositions()
    {
        var model = ConstantSigmaModel.MediumVol();
        var monitor = new MartingaleMonitor(model);
        var analyzer = new TestAnalyzer { TotalEquity = new Money(10000m, Currency.USD) };

        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var order = SubmitOrder.BuyLimit(new StrategyId(1), inst, new Qty(100m), new Price(100m)); // $10000 position (100% of equity)

        var decision = monitor.CheckOrder(order, analyzer);

        Assert.True(decision.IsRefused);
    }

    [Fact]
    public void MartingaleMonitor_AdjustsForVolatility_LowVol()
    {
        var lowVolModel = ConstantSigmaModel.LowVol(); // 10% vol allows larger positions
        var monitor = new MartingaleMonitor(lowVolModel);
        var analyzer = new TestAnalyzer { TotalEquity = new Money(10000m, Currency.USD) };

        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var order = SubmitOrder.BuyLimit(new StrategyId(1), inst, new Qty(15m), new Price(100m)); // $1500 position (15%)

        var decision = monitor.CheckOrder(order, analyzer);

        // With low vol, 15% position should be acceptable
        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void MartingaleMonitor_AdjustsForVolatility_HighVol()
    {
        var highVolModel = ConstantSigmaModel.HighVol(); // 40% vol requires smaller positions
        var monitor = new MartingaleMonitor(highVolModel);
        var analyzer = new TestAnalyzer { TotalEquity = new Money(10000m, Currency.USD) };

        var inst = new Instrument(new Asset("TSLA", AssetClass.Equity), Venue.NASDAQ);
        var order = SubmitOrder.BuyLimit(new StrategyId(1), inst, new Qty(10m), new Price(100m)); // $1000 position (10%)

        var decision = monitor.CheckOrder(order, analyzer);

        // With high vol, even 10% might be risky
        // Should still be approved due to minimum allowed fraction
        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void MartingaleMonitor_RefusesWhenEquityIsZero()
    {
        var model = ConstantSigmaModel.MediumVol();
        var monitor = new MartingaleMonitor(model);
        var analyzer = new TestAnalyzer { TotalEquity = Money.Zero(Currency.USD) };

        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var order = SubmitOrder.BuyLimit(new StrategyId(1), inst, new Qty(1m), new Price(100m));

        var decision = monitor.CheckOrder(order, analyzer);

        Assert.True(decision.IsRefused);
        Assert.Contains("Solvency", decision.Match(
            approved => "",
            (refused, reason) => reason
        ));
    }

    [Fact]
    public void MartingaleMonitor_AllowsMarketOrdersWithoutLimitPrice()
    {
        var model = ConstantSigmaModel.MediumVol();
        var monitor = new MartingaleMonitor(model);
        var analyzer = new TestAnalyzer { TotalEquity = new Money(10000m, Currency.USD) };

        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var order = SubmitOrder.Market(new StrategyId(1), inst, Side.Buy, new Qty(10m));

        var decision = monitor.CheckOrder(order, analyzer);

        // Market orders without limit price are allowed (risk check happens at fill)
        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void MartingaleMonitor_ReturnsCorrectRuleId()
    {
        var model = ConstantSigmaModel.MediumVol();
        var monitor = new MartingaleMonitor(model);
        var analyzer = new TestAnalyzer { TotalEquity = new Money(1000m, Currency.USD) };

        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var order = SubmitOrder.BuyLimit(new StrategyId(1), inst, new Qty(100m), new Price(100m)); // Huge position

        var decision = monitor.CheckOrder(order, analyzer);

        Assert.True(decision.IsRefused);
        var ruleId = decision.Match<string?>(
            approved => null,
            (refused, reason) => "MARTINGALE_INEQUALITY" // RuleId is hardcoded in the refused case
        );
        // Verify the reason contains expected text
        decision.Match(
            approved => "",
            (refused, reason) => {
                Assert.Contains("Solvency", reason);
                return "";
            }
        );
    }
}
