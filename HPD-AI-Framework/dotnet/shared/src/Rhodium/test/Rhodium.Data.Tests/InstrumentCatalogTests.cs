using Rhodium.Primitives;

namespace Rhodium.Data.Tests;

public class InstrumentCatalogTests
{
    [Fact]
    public async Task StaticInstrumentCatalog_GetsContractsByInstrumentAndSymbol()
    {
        var equity = Contracts.Equity("MSFT", Venue.NASDAQ, Currency.USD);
        var index = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m);
        var catalog = new StaticInstrumentCatalog([equity, index]);

        Assert.Equal(equity, await catalog.GetAsync(equity.Instrument));
        Assert.Equal(index, await catalog.GetAsync("SPX"));
        Assert.Null(await catalog.GetAsync("MISSING"));
    }

    [Fact]
    public async Task StaticInstrumentCatalog_SearchesComposableContractDimensions()
    {
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), "CBOE");
        var equity = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var option = Contracts.OptionContract(
            "SPX-C-5000",
            "CBOE",
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(5000m, Currency.USD),
            expiry: Instant.FromUnixSeconds(1_796_016_000),
            right: OptionRight.Call,
            exercise: ExerciseStyle.European);
        var observable = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m);
        var cfd = Contracts.Cfd("AAPL-CFD", "OTC", equity, Currency.USD, tick: 0.01m, lot: 1m, multiplier: 1m);
        var binary = Contracts.BinaryOption("EVENT-YES", "PM", "event:yes", Currency.USD, Money.USD(1m), Instant.FromUnixSeconds(1_796_016_000));
        var catalog = new StaticInstrumentCatalog([option, observable, cfd, binary]);

        var options = await CollectAsync(catalog.SearchAsync(
            assetClass: AssetClass.Option,
            query: new ContractQuery { HasOptionPayoff = true }));
        var observables = await CollectAsync(catalog.SearchAsync(
            query: new ContractQuery { Tradable = false, Observable = true }));
        var expiring = await CollectAsync(catalog.SearchAsync(
            underlying: underlying,
            query: new ContractQuery { Expiring = true }));
        var cfds = await CollectAsync(catalog.SearchAsync(
            query: new ContractQuery { HasCfdPayoff = true, HasLegs = true }));
        var binaries = await CollectAsync(catalog.SearchAsync(
            query: new ContractQuery { HasBinaryPayoff = true }));

        Assert.Equal(option, Assert.Single(options));
        Assert.Equal(observable, Assert.Single(observables));
        Assert.Equal(option, Assert.Single(expiring));
        Assert.Equal(cfd, Assert.Single(cfds));
        Assert.Equal(binary, Assert.Single(binaries));
    }

    private static async Task<List<InstrumentContract>> CollectAsync(IAsyncEnumerable<InstrumentContract> source)
    {
        var contracts = new List<InstrumentContract>();
        await foreach (var contract in source)
            contracts.Add(contract);

        return contracts;
    }
}
