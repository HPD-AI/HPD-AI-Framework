using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class PnLCalculatorTests
{
    [Fact]
    public void PnLCalculator_SpotLongProfit()
    {
        var entry = new Price(100m);
        var exit = new Price(110m);
        var qty = new Qty(10m);

        var pnl = PnLCalculator.Calculate(ContractType.Spot, entry, exit, qty);

        Assert.Equal(100m, pnl.Amount); // (110 - 100) * 10 = 100
        Assert.Equal(exit.Currency, pnl.Currency);
    }

    [Fact]
    public void PnLCalculator_SpotLongLoss()
    {
        var entry = new Price(100m);
        var exit = new Price(90m);
        var qty = new Qty(10m);

        var pnl = PnLCalculator.Calculate(ContractType.Spot, entry, exit, qty);

        Assert.Equal(-100m, pnl.Amount); // (90 - 100) * 10 = -100
    }

    [Fact]
    public void PnLCalculator_LinearPerpWithMultiplier()
    {
        var entry = new Price(50000m);
        var exit = new Price(51000m);
        var qty = new Qty(1m);
        var multiplier = 0.001m; // BTC perpetual contract multiplier

        var pnl = PnLCalculator.Calculate(ContractType.LinearPerp, entry, exit, qty, multiplier);

        Assert.Equal(1m, pnl.Amount); // (51000 - 50000) * 1 * 0.001 = 1
    }

    [Fact]
    public void PnLCalculator_LinearPerpDefaultMultiplier()
    {
        var entry = new Price(100m);
        var exit = new Price(120m);
        var qty = new Qty(5m);

        var pnl = PnLCalculator.Calculate(ContractType.LinearPerp, entry, exit, qty);

        Assert.Equal(100m, pnl.Amount); // (120 - 100) * 5 * 1 = 100
    }

    [Fact]
    public void PnLCalculator_InversePerpProfit()
    {
        var entry = new Price(10000m);
        var exit = new Price(11000m);
        var qty = new Qty(100m);
        var multiplier = 1m;

        var pnl = PnLCalculator.Calculate(ContractType.InversePerp, entry, exit, qty, multiplier);

        // (1/10000 - 1/11000) * 100 * 1 = (0.0001 - 0.00009090909...) * 100
        // = 0.00000909090909... * 100 = 0.000909090909...
        Assert.True(pnl.Amount > 0);
        Assert.Equal(0.0009090909090909090909090900m, pnl.Amount, precision: 10);
    }

    [Fact]
    public void PnLCalculator_InversePerpLoss()
    {
        var entry = new Price(10000m);
        var exit = new Price(9000m);
        var qty = new Qty(100m);
        var multiplier = 1m;

        var pnl = PnLCalculator.Calculate(ContractType.InversePerp, entry, exit, qty, multiplier);

        // (1/10000 - 1/9000) * 100 = (0.0001 - 0.000111...) * 100 = -0.00111... * 100 = -1.11...
        Assert.True(pnl.Amount < 0);
    }

    [Fact]
    public void PnLCalculator_InversePerpZeroEntryPrice()
    {
        var entry = new Price(0m);
        var exit = new Price(10000m);
        var qty = new Qty(100m);

        var pnl = PnLCalculator.Calculate(ContractType.InversePerp, entry, exit, qty);

        Assert.Equal(0m, pnl.Amount); // Division by zero protection
    }

    [Fact]
    public void PnLCalculator_InversePerpZeroExitPrice()
    {
        var entry = new Price(10000m);
        var exit = new Price(0m);
        var qty = new Qty(100m);

        var pnl = PnLCalculator.Calculate(ContractType.InversePerp, entry, exit, qty);

        Assert.Equal(0m, pnl.Amount); // Division by zero protection
    }

    [Fact]
    public void PnLCalculator_OptionThrowsException()
    {
        var entry = new Price(100m);
        var exit = new Price(110m);
        var qty = new Qty(10m);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PnLCalculator.Calculate(ContractType.Option, entry, exit, qty));

        Assert.Contains("Options require Greeks-based valuation", exception.Message);
    }

    [Fact]
    public void PnLCalculator_FutureReturnsZero()
    {
        // Future is not explicitly handled in the switch, should default to 0
        var entry = new Price(100m);
        var exit = new Price(110m);
        var qty = new Qty(10m);

        var pnl = PnLCalculator.Calculate(ContractType.Future, entry, exit, qty);

        Assert.Equal(0m, pnl.Amount);
    }

    [Fact]
    public void PnLCalculator_PreservesCurrency()
    {
        var entry = new Price(100m, Currency.EUR);
        var exit = new Price(110m, Currency.EUR);
        var qty = new Qty(10m);

        var pnl = PnLCalculator.Calculate(ContractType.Spot, entry, exit, qty);

        Assert.Equal(Currency.EUR, pnl.Currency);
    }
}
