using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Options.Tests;

public class OptionChainProviderTests
{
    [Fact]
    public async Task InMemoryProvider_BuildsChainSeriesFromContracts()
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.Now + Duration.FromDays(60);
        var contracts = new[]
        {
            Call("AAPL-20260717-250-C", underlying, expiry, 250m),
            Put("AAPL-20260717-250-P", underlying, expiry, 250m),
            Call("AAPL-20260821-260-C", underlying, expiry + Duration.FromDays(35), 260m)
        };
        var provider = new InMemoryOptionChainProvider(contracts);

        var chain = await provider.GetChainAsync(underlying, "OPRA");

        Assert.NotNull(chain);
        Assert.Equal(underlying, chain.Underlying);
        Assert.Equal("OPRA", chain.Venue.Name);
        Assert.Equal(2, chain.Series.Count);
        Assert.Contains(chain.Series, series => series.Expiration == expiry);
        Assert.Contains(chain.Series, series => series.Expiration == expiry + Duration.FromDays(35));
        Assert.All(chain.Series, series => Assert.Equal(OptionExpirationCycle.Standard, series.ExpirationCycle));
    }

    [Fact]
    public async Task InMemoryProvider_SearchFiltersByRightExpirationAndStrike()
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.Now + Duration.FromDays(60);
        var contracts = new[]
        {
            Call("AAPL-20260717-240-C", underlying, expiry, 240m),
            Call("AAPL-20260717-250-C", underlying, expiry, 250m),
            Put("AAPL-20260717-250-P", underlying, expiry, 250m),
            Call("AAPL-20260821-260-C", underlying, expiry + Duration.FromDays(35), 260m)
        };
        var provider = new InMemoryOptionChainProvider(contracts);

        var matches = await ToListAsync(provider.SearchContractsAsync(new OptionChainQuery
        {
            Underlying = underlying,
            Venue = "OPRA",
            Expiration = DateRange.Between(expiry - Duration.FromDays(1), expiry + Duration.FromDays(1)),
            Right = OptionRight.Call,
            MinStrike = new Price(245m, Currency.USD),
            MaxStrike = new Price(255m, Currency.USD)
        }));

        var match = Assert.Single(matches);
        Assert.Equal("AAPL-20260717-250-C", match.Instrument.Asset.Symbol);
    }

    [Fact]
    public async Task InMemoryProvider_SearchFiltersByTypedExpirationCycle()
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.Now + Duration.FromDays(60);
        var contracts = new[]
        {
            Call("AAPL-STANDARD-250-C", underlying, expiry, 250m, OptionExpirationCycle.Standard),
            Call("AAPL-WEEKLY-250-C", underlying, expiry, 250m, OptionExpirationCycle.Weekly),
            Call("AAPL-QUARTERLY-250-C", underlying, expiry, 250m, OptionExpirationCycle.Quarterly)
        };
        var provider = new InMemoryOptionChainProvider(contracts);

        var matches = await ToListAsync(provider.SearchContractsAsync(new OptionChainQuery
        {
            Underlying = underlying,
            Venue = "OPRA",
            ExpirationCycles = new HashSet<OptionExpirationCycle> { OptionExpirationCycle.Weekly }
        }));

        var match = Assert.Single(matches);
        Assert.Equal("AAPL-WEEKLY-250-C", match.Instrument.Asset.Symbol);
        var terms = Assert.IsType<PayoffTerms.Option>(match.Payoff).Terms;
        Assert.Equal(OptionExpirationCycle.Weekly, terms.ExpirationCycle);
    }

    [Fact]
    public void OptionMarketState_CarriesObservedTheoreticalValueSeparatelyFromContract()
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var contract = Call("AAPL-20260717-250-C", underlying, Instant.Now + Duration.FromDays(60), 250m);

        var state = new OptionMarketState(
            contract.Instrument,
            Timestamp: Instant.Now,
            Last: new Price(3.20m, Currency.USD),
            UnderlyingMark: new Price(255m, Currency.USD),
            ObservedImpliedVolatility: 0.25m,
            ObservedGreeks: new OptionGreeks(0.55m, 0.02m, -0.01m, 0.10m, 0.03m),
            TheoreticalValue: new Money(330m, Currency.USD));

        Assert.Equal(new Money(330m, Currency.USD), state.TheoreticalValue);
        Assert.IsType<PayoffTerms.Option>(contract.Payoff);
    }

    [Fact]
    public async Task OptionChainWorkflow_EnrichesSnapshotWithAnalyticsAndPortfolioGreeks()
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var now = Instant.Now;
        var expiry = now + Duration.FromDays(60);
        var call = European("AAPL-20260717-250-C", underlying, expiry, 250m, OptionRight.Call);
        var put = European("AAPL-20260717-240-P", underlying, expiry, 240m, OptionRight.Put);
        var workflow = new OptionChainWorkflow(new InMemoryOptionChainProvider([call, put]));

        var snapshot = await workflow.BuildSnapshotAsync(new OptionChainWorkflowRequest(
            new OptionChainQuery
            {
                Underlying = underlying,
                Venue = "OPRA",
                Expiration = DateRange.Between(expiry - Duration.FromDays(1), expiry + Duration.FromDays(1))
            },
            now,
            new Price(252m, Currency.USD),
            new Dictionary<Instrument, OptionMarketState>
            {
                [call.Instrument] = new OptionMarketState(
                    call.Instrument,
                    now,
                    Last: new Price(8m, Currency.USD)),
                [put.Instrument] = new OptionMarketState(
                    put.Instrument,
                    now,
                    Last: new Price(3m, Currency.USD))
            },
            new OptionPricingScenario(RiskFreeRate: 0.05m, VolatilityOverride: 0.20m)));

        Assert.Equal(2, snapshot.Chain.Contracts.Count);
        Assert.All(snapshot.Chain.Contracts, state =>
        {
            Assert.NotNull(state.UnderlyingMark);
            Assert.NotNull(state.ObservedGreeks);
            Assert.NotNull(state.TheoreticalValue);
            Assert.True(state.ObservedImpliedVolatility > 0m);
        });
        var aggregate = snapshot.AggregatePortfolioGreeks(new Dictionary<Instrument, Qty>
        {
            [call.Instrument] = new Qty(2m),
            [put.Instrument] = new Qty(-1m)
        });

        Assert.Equal(Currency.USD, aggregate.MarketValue.Currency);
        Assert.NotEqual(0m, aggregate.Delta);
        Assert.NotEqual(0m, aggregate.Vega);
    }

    private static InstrumentContract Call(
        string symbol,
        Instrument underlying,
        Instant expiry,
        decimal strike,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard) =>
        Rhodium.Primitives.Contracts.OptionContract(
            symbol,
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(strike, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American,
            expirationCycle: expirationCycle);

    private static InstrumentContract Put(
        string symbol,
        Instrument underlying,
        Instant expiry,
        decimal strike,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard) =>
        Rhodium.Primitives.Contracts.OptionContract(
            symbol,
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(strike, Currency.USD),
            expiry,
            OptionRight.Put,
            ExerciseStyle.American,
            expirationCycle: expirationCycle);

    private static InstrumentContract European(
        string symbol,
        Instrument underlying,
        Instant expiry,
        decimal strike,
        OptionRight right) =>
        Rhodium.Primitives.Contracts.OptionContract(
            symbol,
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(strike, Currency.USD),
            expiry,
            right,
            ExerciseStyle.European);

    private static async Task<List<InstrumentContract>> ToListAsync(IAsyncEnumerable<InstrumentContract> source)
    {
        var result = new List<InstrumentContract>();
        await foreach (var item in source)
            result.Add(item);
        return result;
    }
}
