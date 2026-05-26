using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Options.Tests;

public class PortfolioGreeksTests
{
    [Fact]
    public void DefaultPortfolioGreeksModel_ScalesGreeksByQuantityAndContractMultiplier()
    {
        var call = Option("AAPL-250-C", OptionRight.Call, 250m);
        var put = Option("AAPL-240-P", OptionRight.Put, 240m);
        var positions = new[]
        {
            new OptionPositionRisk(
                call,
                new Qty(2m),
                new OptionGreeks(
                    Delta: 0.50m,
                    Gamma: 0.02m,
                    Theta: -0.03m,
                    Vega: 0.10m,
                    Rho: 0.04m,
                    Vanna: 0.01m,
                    Charm: -0.02m,
                    Vomma: 0.03m),
                new Money(600m, Currency.USD)),
            new OptionPositionRisk(
                put,
                new Qty(-1m),
                new OptionGreeks(
                    Delta: -0.40m,
                    Gamma: 0.03m,
                    Theta: -0.02m,
                    Vega: 0.08m,
                    Rho: -0.05m,
                    Vanna: -0.01m,
                    Charm: 0.01m,
                    Vomma: 0.02m),
                new Money(-250m, Currency.USD))
        };

        var aggregate = DefaultPortfolioGreeksModel.Instance.Aggregate(
            positions,
            new OptionPortfolioScenario());

        Assert.Equal(140m, aggregate.Delta);
        Assert.Equal(1m, aggregate.Gamma);
        Assert.Equal(-4m, aggregate.Theta);
        Assert.Equal(12m, aggregate.Vega);
        Assert.Equal(13m, aggregate.Rho);
        Assert.Equal(350m, aggregate.MarketValue.Amount);
        Assert.Equal(3m, aggregate.Vanna);
        Assert.Equal(-5m, aggregate.Charm);
        Assert.Equal(4m, aggregate.Vomma);
    }

    [Fact]
    public void DefaultPortfolioGreeksModel_RequiresSingleMarketValueCurrency()
    {
        var positions = new[]
        {
            new OptionPositionRisk(
                Option("AAPL-250-C", OptionRight.Call, 250m),
                new Qty(1m),
                new OptionGreeks(0.50m, 0.02m, -0.03m, 0.10m, 0.04m),
                new Money(300m, Currency.USD)),
            new OptionPositionRisk(
                Option("AAPL-260-C", OptionRight.Call, 260m),
                new Qty(1m),
                new OptionGreeks(0.45m, 0.02m, -0.02m, 0.08m, 0.03m),
                new Money(250m, Currency.EUR))
        };

        Assert.Throws<InvalidOperationException>(() =>
            DefaultPortfolioGreeksModel.Instance.Aggregate(
                positions,
                new OptionPortfolioScenario()));
    }

    [Fact]
    public void DefaultPortfolioGreeksModel_DoesNotInferWeightedConventions()
    {
        var positions = new[]
        {
            new OptionPositionRisk(
                Option("AAPL-250-C", OptionRight.Call, 250m),
                new Qty(1m),
                new OptionGreeks(0.50m, 0.02m, -0.03m, 0.10m, 0.04m),
                new Money(300m, Currency.USD))
        };

        Assert.Throws<InvalidOperationException>(() =>
            DefaultPortfolioGreeksModel.Instance.Aggregate(
                positions,
                new OptionPortfolioScenario(GreeksConvention.BetaWeighted)));
    }

    private static InstrumentContract Option(string symbol, OptionRight right, decimal strike)
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        return Rhodium.Primitives.Contracts.OptionContract(
            symbol,
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(strike, Currency.USD),
            Instant.Now + Duration.FromDays(60),
            right,
            ExerciseStyle.American);
    }
}
