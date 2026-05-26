using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class InstrumentValuationTests
{
    private static readonly IInstrumentValuationModel Valuation = DefaultInstrumentValuationModel.Instance;

    [Fact]
    public void RealizedPnL_SpotLongProfit()
    {
        var contract = Contracts.Equity("SPY", Venue.NYSE, Currency.USD);

        var pnl = Valuation.RealizedPnL(contract, new Qty(10m), new Price(100m, Currency.USD), new Price(110m, Currency.USD));

        Assert.Equal(100m, pnl.Amount);
        Assert.Equal(Currency.USD, pnl.Currency);
    }

    [Fact]
    public void RealizedPnL_SpotLongLoss()
    {
        var contract = Contracts.Equity("SPY", Venue.NYSE, Currency.USD);

        var pnl = Valuation.RealizedPnL(contract, new Qty(10m), new Price(100m, Currency.USD), new Price(90m, Currency.USD));

        Assert.Equal(-100m, pnl.Amount);
    }

    [Fact]
    public void LinearContract_UsesMultiplierForNotionalMarketValueAndPnL()
    {
        var underlying = new Instrument(new Asset("ES", AssetClass.Index), Venue.CME);
        var contract = Contracts.Future(
            "ESZ6",
            Venue.CME,
            underlying,
            Currency.USD,
            tick: 0.25m,
            lot: 1m,
            multiplier: 50m,
            expiry: Instant.FromUnixSeconds(1_796_016_000));

        var notional = Valuation.Notional(contract, new Qty(2m), new Price(5010m, Currency.USD));
        var marketValue = Valuation.MarketValue(contract, new Qty(2m), new Price(5010m, Currency.USD));
        var unrealized = Valuation.UnrealizedPnL(contract, new Qty(2m), new Price(5000m, Currency.USD), new Price(5010m, Currency.USD));

        Assert.Equal(501000m, notional.Amount);
        Assert.Equal(501000m, marketValue.Amount);
        Assert.Equal(1000m, unrealized.Amount);
    }

    [Fact]
    public void RealizedPnL_LinearPerpetualUsesMultiplier()
    {
        var contract = Contracts.CryptoPerpetual(
            "BTC-PERP",
            Venue.CME,
            Currency.BTC,
            Currency.USD,
            Currency.USD,
            tick: 0.5m,
            lot: 1m,
            multiplier: 0.001m,
            inverse: false);

        var pnl = Valuation.RealizedPnL(contract, new Qty(1m), new Price(50000m, Currency.USD), new Price(51000m, Currency.USD));

        Assert.Equal(1m, pnl.Amount);
        Assert.Equal(Currency.USD, pnl.Currency);
    }

    [Fact]
    public void InverseContract_UsesSettlementCurrencyForNotionalAndPnL()
    {
        var contract = Contracts.CryptoPerpetual(
            "BTCUSD-PERP",
            Venue.CME,
            Currency.BTC,
            Currency.USD,
            Currency.BTC,
            tick: 0.5m,
            lot: 1m,
            multiplier: 1m,
            inverse: true);

        var notional = Valuation.Notional(contract, new Qty(100m), new Price(10000m, Currency.USD));
        var pnl = Valuation.RealizedPnL(contract, new Qty(100m), new Price(10000m, Currency.USD), new Price(11000m, Currency.USD));

        Assert.Equal(0.01m, notional.Amount);
        Assert.Equal(Currency.BTC, notional.Currency);
        Assert.True(pnl.Amount > 0);
        Assert.Equal(0.0009090909090909090909090900m, pnl.Amount, precision: 10);
        Assert.Equal(Currency.BTC, pnl.Currency);
    }

    [Fact]
    public void InverseContract_ZeroPriceReturnsZero()
    {
        var contract = Contracts.CryptoPerpetual(
            "BTCUSD-PERP",
            Venue.CME,
            Currency.BTC,
            Currency.USD,
            Currency.BTC,
            tick: 0.5m,
            lot: 1m,
            multiplier: 1m,
            inverse: true);

        var pnl = Valuation.RealizedPnL(contract, new Qty(100m), new Price(0m, Currency.USD), new Price(10000m, Currency.USD));

        Assert.Equal(0m, pnl.Amount);
    }

    [Fact]
    public void QuantoContract_UsesConversionRate()
    {
        var contract = new InstrumentContract
        {
            Instrument = new Instrument(new Asset("NQ-QUANTO", AssetClass.Future), Venue.CME),
            Grid = new TradingGrid(0.25m, 1m),
            Exposure = new EconomicExposure.Quanto(Currency.USD, Currency.USD, Currency.EUR, 10m, 0.9m),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Cash(Currency.EUR, SettlementDelay.TPlus(1, Venue.CME.Name)),
            Margin = new MarginTerms.FixedFraction(0.1m, 0.05m),
            Fees = FeeTerms.None
        };

        var pnl = Valuation.RealizedPnL(contract, new Qty(2m), new Price(100m, Currency.USD), new Price(110m, Currency.USD));

        Assert.Equal(180m, pnl.Amount);
        Assert.Equal(Currency.EUR, pnl.Currency);
    }

    [Fact]
    public void OptionExpiryPayoff_UsesIntrinsicValueAndMultiplier()
    {
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), Venue.CME);
        var contract = Contracts.IndexOption(
            "SPX-C-5000",
            Venue.CME,
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(5000m, Currency.USD),
            expiry: Instant.FromUnixSeconds(1_796_016_000),
            right: OptionRight.Call);

        var payoff = Valuation.ExpiryPayoff(contract, new Qty(2m), new Price(5025m, Currency.USD));

        Assert.Equal(5000m, payoff.Amount);
        Assert.Equal(Currency.USD, payoff.Currency);
    }

    [Fact]
    public void InverseCryptoOptionExpiryPayoff_UsesInverseExposure()
    {
        var underlying = Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m).Instrument;
        var contract = Contracts.InverseCryptoOption(
            "BTCUSD-C-100K",
            "DERIBIT",
            underlying,
            Currency.BTC,
            Currency.USD,
            Currency.BTC,
            tick: 0.1m,
            lot: 0.001m,
            multiplier: 1m,
            strike: new Price(100_000m, Currency.USD),
            expiry: Instant.FromUnixSeconds(1_796_016_000),
            right: OptionRight.Call);

        var payoff = Valuation.ExpiryPayoff(contract, new Qty(2m), new Price(120_000m, Currency.USD));

        Assert.Equal(((1m / 100_000m) - (1m / 120_000m)) * 2m, payoff.Amount, precision: 18);
        Assert.Equal(Currency.BTC, payoff.Currency);
    }

    [Fact]
    public void QuantoCryptoOptionExpiryPayoff_UsesQuantoExposure()
    {
        var underlying = Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m).Instrument;
        var contract = Contracts.QuantoCryptoOption(
            "BTCUSDT-C-100K-USD",
            "DERIBIT",
            underlying,
            Currency.BTC,
            Currency.USDT,
            Currency.USD,
            conversionRate: 0.5m,
            tick: 0.1m,
            lot: 0.001m,
            multiplier: 1m,
            strike: new Price(100_000m, Currency.USDT),
            expiry: Instant.FromUnixSeconds(1_796_016_000),
            right: OptionRight.Call);

        var payoff = Valuation.ExpiryPayoff(contract, new Qty(2m), new Price(120_000m, Currency.USDT));

        Assert.Equal(20_000m, payoff.Amount);
        Assert.Equal(Currency.USD, payoff.Currency);
    }

    [Fact]
    public void BinaryExpiryPayoff_PaysOnlyWhenOutcomeOccurs()
    {
        var contract = Contracts.BinaryOption(
            "EVENT:YES",
            Venue.CME,
            "event:yes",
            Currency.USD,
            Money.USD(100m),
            eventTime: null);

        var winningPayoff = Valuation.ExpiryPayoff(contract, new Qty(3m), new Price(0m, Currency.USD), outcome: true);
        var losingPayoff = Valuation.ExpiryPayoff(contract, new Qty(3m), new Price(0m, Currency.USD), outcome: false);

        Assert.Equal(300m, winningPayoff.Amount);
        Assert.Equal(0m, losingPayoff.Amount);
    }

    [Fact]
    public void BettingExpiryPayoff_UsesEntryDecimalOddsOnlyWhenSelectionWins()
    {
        var contract = Contracts.BettingInstrument(
            "MATCH:TEAM-A",
            new Venue("PM"),
            "match-1",
            "team-a",
            Currency.USD,
            tick: 0.01m,
            eventTime: null);

        var winningPayoff = Valuation.ExpiryPayoff(contract, new Qty(10m), new Price(3.5m, Currency.USD), outcome: true);
        var losingPayoff = Valuation.ExpiryPayoff(contract, new Qty(10m), new Price(3.5m, Currency.USD), outcome: false);
        var notional = Valuation.Notional(contract, new Qty(10m), new Price(3.5m, Currency.USD));
        var openMarketValue = Valuation.MarketValue(contract, new Qty(10m), new Price(3.5m, Currency.USD));

        Assert.Equal(35m, winningPayoff.Amount);
        Assert.Equal(0m, losingPayoff.Amount);
        Assert.Equal(10m, notional.Amount);
        Assert.Equal(0m, openMarketValue.Amount);
        Assert.Equal(Currency.USD, winningPayoff.Currency);
    }

    [Fact]
    public void ObservableReference_HasNoTradableValueOrPnL()
    {
        var contract = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m);

        var notional = Valuation.Notional(contract, new Qty(1m), new Price(5000m, Currency.USD));
        var marketValue = Valuation.MarketValue(contract, new Qty(1m), new Price(5000m, Currency.USD));
        var pnl = Valuation.RealizedPnL(contract, new Qty(1m), new Price(5000m, Currency.USD), new Price(5010m, Currency.USD));

        Assert.Equal(0m, notional.Amount);
        Assert.Equal(0m, marketValue.Amount);
        Assert.Equal(0m, pnl.Amount);
        Assert.Equal(Currency.USD, pnl.Currency);
    }

    [Fact]
    public void QuotedSpread_UsesFormulaExposureAsTradableSpreadPrice()
    {
        var underlying = new Instrument(new Asset("ES", AssetClass.Index), Venue.CME);
        var front = Contracts.Future(
            "ESM6",
            Venue.CME,
            underlying,
            Currency.USD,
            tick: 0.25m,
            lot: 1m,
            multiplier: 50m,
            expiry: Instant.FromUnixSeconds(1_780_000_000));
        var back = Contracts.Future(
            "ESU6",
            Venue.CME,
            underlying,
            Currency.USD,
            tick: 0.25m,
            lot: 1m,
            multiplier: 50m,
            expiry: Instant.FromUnixSeconds(1_788_000_000));
        var spread = Contracts.FuturesSpread(
            "ES-CALENDAR",
            Venue.CME,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            [
                new InstrumentLeg(front.Instrument, 1m, Side.Buy),
                new InstrumentLeg(back.Instrument, 1m, Side.Sell)
            ]);

        var notional = Valuation.Notional(spread, new Qty(3m), new Price(12.50m, Currency.USD));
        var marketValue = Valuation.MarketValue(spread, new Qty(3m), new Price(13.00m, Currency.USD));
        var pnl = Valuation.RealizedPnL(spread, new Qty(3m), new Price(12.50m, Currency.USD), new Price(13.00m, Currency.USD));

        Assert.Equal(37.50m, notional.Amount);
        Assert.Equal(39.00m, marketValue.Amount);
        Assert.Equal(1.50m, pnl.Amount);
        Assert.Equal(Currency.USD, pnl.Currency);
    }

    [Fact]
    public void ValuePosition_ReturnsContractAwareProjection()
    {
        var contract = Contracts.Cfd(
            "SPY-CFD",
            Venue.CME,
            new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE),
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 1m);
        var position = new PositionValuationInput(contract.Instrument, new Qty(10m), new Price(100m, Currency.USD), Money.USD(5m));

        var value = Valuation.ValuePosition(contract, position, new Price(110m, Currency.USD));

        Assert.Equal(1100m, value.Notional.Amount);
        Assert.Equal(1100m, value.MarketValue.Amount);
        Assert.Equal(100m, value.UnrealizedPnL.Amount);
        Assert.Equal(5m, value.RealizedPnL.Amount);
    }
}
