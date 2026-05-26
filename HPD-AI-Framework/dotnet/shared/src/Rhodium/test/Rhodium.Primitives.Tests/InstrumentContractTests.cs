using System.Text.Json;
using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class InstrumentContractTests
{
    [Fact]
    public void EquityRecipe_CreatesSpotTradableContract()
    {
        var contract = Contracts.Equity("MSFT", Venue.NASDAQ, Currency.USD, tick: 0.01m, lot: 1m);

        Assert.Equal(AssetClass.Equity, contract.Instrument.Asset.Class);
        Assert.Equal(EconomicExposureKind.Spot, contract.Exposure.Kind());
        Assert.Equal(0.01m, contract.Grid.PriceIncrement);
        Assert.True(contract.VenueRules.IsTradable);
        Assert.True(contract.VenueRules.SupportsExecution);
        Assert.IsType<FinancingTerms.Borrow>(contract.Financing);
        Assert.IsType<DataSemantics.Tradable>(contract.Data);
    }

    [Fact]
    public void IndexRecipe_CreatesObservableReferenceContract()
    {
        var contract = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m);

        Assert.Equal(AssetClass.Index, contract.Instrument.Asset.Class);
        Assert.Equal(EconomicExposureKind.Reference, contract.Exposure.Kind());
        Assert.False(contract.VenueRules.IsTradable);
        Assert.False(contract.VenueRules.SupportsExecution);
        var data = Assert.IsType<DataSemantics.Observable>(contract.Data);
        Assert.Equal(ObservableKind.IndexLevel, data.Kind);
    }

    [Fact]
    public void PerpetualRecipe_CanRepresentInverseSettlement()
    {
        var contract = Contracts.CryptoPerpetual(
            "BTCUSD-PERP",
            Venue.Binance,
            Currency.BTC,
            Currency.USD,
            Currency.BTC,
            tick: 0.1m,
            lot: 1m,
            multiplier: 1m,
            inverse: true);

        Assert.Equal(EconomicExposureKind.Inverse, contract.Exposure.Kind());
        Assert.Equal(Currency.BTC, contract.Exposure.SettlementCurrency());
        Assert.IsType<ContractLifecycle.Perpetual>(contract.Lifecycle);
        Assert.IsType<FinancingTerms.PerpetualFunding>(contract.Financing);
    }

    [Fact]
    public void FinancingChargeCommand_CanBeDerivedFromContractFinancingTerms()
    {
        var strategyId = new StrategyId(7);
        var effectiveAt = Instant.FromUnixSeconds(10);
        var equity = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD);
        var fx = Contracts.CurrencyPair("EURUSD", "IDEALPRO", Currency.EUR, Currency.USD, 0.0001m, 1000m);
        var perp = Contracts.CryptoPerpetual(
            "BTCUSD-PERP",
            Venue.Binance,
            Currency.BTC,
            Currency.USD,
            Currency.USD,
            tick: 0.1m,
            lot: 0.001m,
            multiplier: 1m,
            inverse: false);

        var borrow = FinancingChargeCommand.FromContractRate(
            strategyId,
            equity,
            Money.USD(1_000m),
            rate: 0.002m,
            new Qty(10m),
            effectiveAt: effectiveAt);
        var rollover = FinancingChargeCommand.FromContractRate(
            strategyId,
            fx,
            Money.USD(100_000m),
            rate: -0.000011m,
            new Qty(100_000m),
            effectiveAt: effectiveAt);
        var funding = FinancingChargeCommand.FromContractRate(
            strategyId,
            perp,
            Money.USD(20_000m),
            rate: 0.0001m,
            new Qty(1m),
            effectiveAt: effectiveAt);

        Assert.Equal(FinancingChargeType.BorrowFee, borrow.ChargeType);
        Assert.Equal(Money.USD(-2m), borrow.Amount);
        Assert.Equal("NASDAQ:AAPL:borrow", borrow.ExternalReference);

        Assert.Equal(FinancingChargeType.ForexRollover, rollover.ChargeType);
        Assert.Equal(Money.USD(-1.1m), rollover.Amount);
        Assert.Equal("default-fx-rollover", rollover.ExternalReference);

        Assert.Equal(FinancingChargeType.PerpetualFunding, funding.ChargeType);
        Assert.Equal(Money.USD(-2m), funding.Amount);
        Assert.Equal("Binance:BTCUSD-PERP:funding", funding.ExternalReference);
    }

    [Fact]
    public void ProductRecipes_CoverLeanAndNautilusCategoriesAsContracts()
    {
        var index = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m).Instrument;
        var future = Contracts.Future("ESZ6", Venue.CME, index, Currency.USD, 0.25m, 1m, 50m, Instant.FromUnixSeconds(1_796_016_000)).Instrument;
        var equity = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var btc = Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m).Instrument;
        var equityOption = Contracts.OptionContract("AAPL-C-250", "OPRA", equity, Currency.USD, 0.01m, 1m, 100m, new Price(250m, Currency.USD), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call, ExerciseStyle.American).Instrument;

        var recipes = new[]
        {
            Contracts.Equity("MSFT", Venue.NASDAQ, Currency.USD),
            Contracts.CurrencyPair("EURUSD", "IDEALPRO", Currency.EUR, Currency.USD, 0.0001m, 1000m),
            Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m),
            Contracts.CommoditySpot("XAUUSD", "OTC", Currency.USD, 0.01m, 0.01m),
            Contracts.Index("SPX", "CBOE", Currency.USD, 0.01m),
            Contracts.Future("ESZ6", Venue.CME, index, Currency.USD, 0.25m, 1m, 50m, Instant.FromUnixSeconds(1_796_016_000)),
            Contracts.CryptoFuture("BTC-20261225", Venue.Binance, Currency.BTC, Currency.USDT, Currency.USDT, 0.1m, 0.001m, 1m, Instant.FromUnixSeconds(1_798_156_800), inverse: false),
            Contracts.CryptoPerpetual("BTCUSDT-PERP", Venue.Binance, Currency.BTC, Currency.USDT, Currency.USDT, 0.1m, 0.001m, 1m, inverse: false),
            Contracts.Perpetual("OIL-PERP", "OTC", AssetClass.Commodity, Currency.USD, Currency.USD, Currency.USD, 0.01m, 1m, 100m, inverse: false, "OTC:OIL-PERP:funding"),
            Contracts.OptionContract("AAPL-C-250", "OPRA", equity, Currency.USD, 0.01m, 1m, 100m, new Price(250m, Currency.USD), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call, ExerciseStyle.American),
            Contracts.IndexOption("SPX-C-5000", "CBOE", index, Currency.USD, 0.05m, 1m, 100m, new Price(5000m, Currency.USD), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call),
            Contracts.FutureOption("ESZ6-C-5000", Venue.CME, future, Currency.USD, 0.25m, 1m, 50m, new Price(5000m, Currency.USD), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call),
            Contracts.LinearCryptoOption("BTC-C-100K", "DERIBIT", btc, Currency.USDT, Currency.USDT, 0.1m, 0.001m, 1m, new Price(100_000m, Currency.USDT), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call),
            Contracts.InverseCryptoOption("BTCUSD-C-100K", "DERIBIT", btc, Currency.BTC, Currency.USD, Currency.BTC, 0.1m, 0.001m, 1m, new Price(100_000m, Currency.USD), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call),
            Contracts.QuantoCryptoOption("BTCUSDT-C-100K-USD", "DERIBIT", btc, Currency.BTC, Currency.USDT, Currency.USD, 1m, 0.1m, 0.001m, 1m, new Price(100_000m, Currency.USDT), Instant.FromUnixSeconds(1_796_016_000), OptionRight.Call),
            Contracts.FuturesSpread("ES-CALENDAR", Venue.CME, Currency.USD, 0.25m, 1m, [new InstrumentLeg(future, 1m, Side.Buy), new InstrumentLeg(index, 1m, Side.Sell, LegRole.Reference)]),
            Contracts.OptionSpread("AAPL-VERTICAL", "OPRA", Currency.USD, 0.01m, 1m, [new InstrumentLeg(equityOption, 1m, Side.Buy), new InstrumentLeg(equityOption, 1m, Side.Sell)]),
            Contracts.Synthetic("SPX-BASKET", "SYNTH", Currency.USD, 0.01m, 1m, "0.5*SPY+0.5*QQQ", [new InstrumentLeg(equity, 0.5m, Side.Buy)]),
            Contracts.BinaryOption("EVENT-YES", "PM", "event:yes", Currency.USD, Money.USD(1m), Instant.FromUnixSeconds(1_796_016_000)),
            Contracts.Cfd("AAPL-CFD", "OTC", equity, Currency.USD, 0.01m, 1m, 1m),
            Contracts.BettingInstrument("TEAM-WIN", "BET", "match-1", "team-a", Currency.USD, 0.01m, Instant.FromUnixSeconds(1_796_016_000)),
            Contracts.TokenizedAsset("WBTC", Venue.Binance, AssetClass.Crypto, Currency.USDT, 0.01m, 0.0001m, "ethereum", "0xbtc"),
            Contracts.Observable("SOFR", "FED", Currency.USD, ObservableKind.ReferenceRate)
        };

        foreach (var contract in recipes)
        {
            var validation = InstrumentContractValidator.Validate(contract);
            Assert.True(validation.IsValid, $"{contract.Instrument}: {string.Join(", ", validation.Issues.Select(static issue => issue.Code))}");
        }
    }

    [Fact]
    public void InstrumentContract_RoundTripsThroughJsonWithTypedDimensions()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
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
            new Price(100_000m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call) with
        {
            Grid = new TradingGrid(
                0.1m,
                0.001m,
                PricePrecision: 1,
                SizePrecision: 3,
                LotSize: 0.001m,
                PriceIncrementRule: new PriceIncrementRule.Piecewise([
                    new PriceIncrementBand(null, new Price(10_000m, Currency.USD), 0.5m),
                    new PriceIncrementBand(new Price(10_000m, Currency.USD), null, 1m)
                ])),
            Constraints = new TradingConstraints(
                MinQuantity: new Qty(0.001m),
                MinNotional: Money.USD(10m)),
            Fees = new FeeTerms.MakerTaker(1m, 2m),
            Financing = new FinancingTerms.PerpetualFunding(new FundingSchedule(Duration.FromHours(8)), "deribit:funding"),
            Data = new DataSemantics.Tradable(MarketDataKind.Quote),
            Tags = new Dictionary<string, string> { ["provider"] = "deribit" }
        };

        var json = JsonSerializer.Serialize(contract);
        var roundTripped = JsonSerializer.Deserialize<InstrumentContract>(json);

        Assert.NotNull(roundTripped);
        var validation = InstrumentContractValidator.Validate(roundTripped);
        Assert.True(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
        Assert.Equal(contract.Instrument, roundTripped.Instrument);
        Assert.Equal(EconomicExposureKind.Inverse, roundTripped.Exposure.Kind());
        Assert.IsType<ContractLifecycle.Expiring>(roundTripped.Lifecycle);
        Assert.IsType<SettlementTerms.Cash>(roundTripped.Settlement);
        Assert.IsType<MarginTerms.Portfolio>(roundTripped.Margin);
        Assert.IsType<FeeTerms.MakerTaker>(roundTripped.Fees);
        Assert.IsType<FinancingTerms.PerpetualFunding>(roundTripped.Financing);
        Assert.IsType<PayoffTerms.Option>(roundTripped.Payoff);
        Assert.IsType<PriceIncrementRule.Piecewise>(roundTripped.Grid.PriceIncrementRule);
        Assert.IsType<DataSemantics.Tradable>(roundTripped.Data);
        Assert.Equal("deribit", roundTripped.Tags["provider"]);
    }

    [Fact]
    public void FromIdentity_RejectsAmbiguousProductClasses()
    {
        var crypto = new Instrument(new Asset("BTCUSDT", AssetClass.Crypto), Venue.Binance);
        var option = new Instrument(new Asset("AAPL-C-250", AssetClass.Option), "OPRA");
        var future = new Instrument(new Asset("ESZ6", AssetClass.Future), Venue.CME);

        Assert.Throws<InvalidOperationException>(() => Contracts.FromIdentity(crypto, Currency.USDT));
        Assert.Throws<InvalidOperationException>(() => Contracts.FromIdentity(option, Currency.USD));
        Assert.Throws<InvalidOperationException>(() => Contracts.FromIdentity(future, Currency.USD));
    }

    [Fact]
    public void Validator_RejectsInvalidDimensionCombinations()
    {
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), "CBOE");
        var invalid = Contracts.OptionContract(
            "SPX-C-5000",
            "CBOE",
            underlying,
            Currency.USD,
            0.05m,
            1m,
            100m,
            new Price(5000m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call,
            ExerciseStyle.European) with
        {
            Lifecycle = new ContractLifecycle.Cash(),
            Legs = []
        };

        var validation = InstrumentContractValidator.Validate(invalid);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Code == "payoff.option.lifecycle.invalid");
        Assert.Contains(validation.Issues, issue => issue.Code == "payoff.option.underlying.missing");
    }

    [Fact]
    public void OptionRecipe_UsesTypedOptionTermsInsteadOfLoosePayoffFields()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var activation = Instant.FromUnixSeconds(1_765_000_000);
        var contract = Contracts.OptionContract(
            "AAPL-20261225-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American,
            unitOfTrade: 50m,
            activation: activation);

        var payoff = Assert.IsType<PayoffTerms.Option>(contract.Payoff);

        Assert.Equal(underlying, payoff.Terms.Underlying);
        Assert.Equal(new Price(250m, Currency.USD), payoff.Terms.Strike.Strike);
        Assert.Equal(new Price(250m, Currency.USD), payoff.Terms.Strike.ScaledStrike);
        Assert.Equal(100m, payoff.Terms.ContractMultiplier);
        Assert.Equal(50m, payoff.Terms.ContractUnitOfTrade);
        Assert.Equal(activation, payoff.Terms.Activation);
        Assert.Equal(expiry, payoff.Terms.Expiration);
        Assert.Equal(OptionExpirationCycle.Standard, payoff.Terms.ExpirationCycle);
        Assert.Equal(OptionPremiumStyle.Upfront, payoff.Terms.PremiumStyle);
        Assert.Equal(OptionExercisePolicy.Manual, payoff.Terms.ExercisePolicy);
        Assert.Empty(payoff.Terms.ExerciseDates);
        Assert.Null(contract.Package);
    }

    [Fact]
    public void OptionPayoff_RejectsNullTerms()
    {
        Assert.Throws<ArgumentNullException>(() => new PayoffTerms.Option(null!));
    }

    [Fact]
    public void OptionStrikeTerms_RejectsNonPositiveStrike()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptionStrikeTerms(new Price(0m, Currency.USD)));

        Assert.Equal("strike", exception.ParamName);
    }

    [Fact]
    public void OptionTerms_RejectsNonPositiveMultiplier()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OptionTerms(
            underlying,
            new OptionStrikeTerms(new Price(250m, Currency.USD)),
            OptionRight.Call,
            ExerciseStyle.American,
            OptionSettlementStyle.Cash,
            Instant.MinValue,
            expiry,
            contractMultiplier: 0m,
            contractUnitOfTrade: 100m,
            OptionExpirationCycle.Standard,
            OptionPremiumStyle.Upfront,
            OptionExercisePolicy.Manual,
            OptionAssignmentPolicy.VenueDefined,
            []));

        Assert.Equal("contractMultiplier", exception.ParamName);
    }

    [Fact]
    public void OptionTerms_RejectsActivationAfterExpiration()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);

        var exception = Assert.Throws<ArgumentException>(() => new OptionTerms(
            underlying,
            new OptionStrikeTerms(new Price(250m, Currency.USD)),
            OptionRight.Call,
            ExerciseStyle.American,
            OptionSettlementStyle.Cash,
            expiry + Duration.FromDays(1),
            expiry,
            contractMultiplier: 100m,
            contractUnitOfTrade: 100m,
            OptionExpirationCycle.Standard,
            OptionPremiumStyle.Upfront,
            OptionExercisePolicy.Manual,
            OptionAssignmentPolicy.VenueDefined,
            []));

        Assert.Equal("activation", exception.ParamName);
    }

    [Fact]
    public void BermudanOption_RequiresExplicitAscendingExerciseSchedule()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var activation = Instant.FromUnixSeconds(1_765_000_000);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var missingSchedule = Assert.Throws<ArgumentException>(() => Contracts.OptionContract(
            "AAPL-BERM-C-250",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.Bermudan,
            activation: activation));

        Assert.Contains("Bermudan options require explicit exercise dates", missingSchedule.Message, StringComparison.Ordinal);

        var validSchedule = Contracts.OptionContract(
            "AAPL-BERM-C-250",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.Bermudan,
            activation: activation,
            exerciseDates:
            [
                Instant.FromUnixSeconds(1_770_000_000),
                Instant.FromUnixSeconds(1_780_000_000),
                expiry
            ]);

        Assert.True(InstrumentContractValidator.Validate(validSchedule).IsValid);
    }

    [Fact]
    public void ExerciseSchedule_IsRejectedForNonBermudanOptions()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var exception = Assert.Throws<ArgumentException>(() => Contracts.OptionContract(
            "AAPL-EU-C-250",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exerciseDates: [expiry]));

        Assert.Contains("Exercise dates are only valid for Bermudan options", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FutureOptionRecipe_DefaultsToFuturesStylePremium()
    {
        var index = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m).Instrument;
        var future = Contracts.Future(
            "ESZ6",
            Venue.CME,
            index,
            Currency.USD,
            0.25m,
            1m,
            50m,
            Instant.FromUnixSeconds(1_796_016_000)).Instrument;

        var contract = Contracts.FutureOption(
            "ESZ6-C-5000",
            Venue.CME,
            future,
            Currency.USD,
            0.25m,
            1m,
            50m,
            new Price(5000m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call);

        var terms = Assert.IsType<PayoffTerms.Option>(contract.Payoff).Terms;
        Assert.Equal(OptionPremiumStyle.FuturesStyle, terms.PremiumStyle);
        Assert.IsType<MarginTerms.Portfolio>(contract.Margin);
    }

    [Fact]
    public void OptionSpreadRecipe_UsesPackageTermsWithoutProductSubclass()
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var longCall = Contracts.OptionContract(
            "AAPL-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American).Instrument;

        var spread = Contracts.OptionSpread(
            "AAPL-CALL-SPREAD",
            "OPRA",
            Currency.USD,
            0.01m,
            1m,
            [new InstrumentLeg(longCall, 1m, Side.Buy), new InstrumentLeg(longCall, 1m, Side.Sell)]);

        Assert.Equal(AssetClass.Option, spread.Instrument.Asset.Class);
        Assert.NotNull(spread.Package);
        Assert.Equal(PackageKind.OptionSpread, spread.Package.Kind);
        Assert.Empty(spread.Tags);
        Assert.IsType<EconomicExposure.Formula>(spread.Exposure);
        Assert.True(InstrumentContractValidator.Validate(spread).IsValid);
    }

    [Fact]
    public void FuturesSpreadRecipe_UsesPackageTermsInsteadOfProductTags()
    {
        var index = Contracts.Index("SPX", "CBOE", Currency.USD, tick: 0.01m).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var front = Contracts.Future("ESM6", Venue.CME, index, Currency.USD, 0.25m, 1m, 50m, expiry).Instrument;
        var back = Contracts.Future("ESU6", Venue.CME, index, Currency.USD, 0.25m, 1m, 50m, Instant.FromUnixSeconds(1_803_792_000)).Instrument;

        var spread = Contracts.FuturesSpread(
            "ES-CALENDAR",
            Venue.CME,
            Currency.USD,
            0.25m,
            1m,
            [new InstrumentLeg(front, 1m, Side.Buy), new InstrumentLeg(back, 1m, Side.Sell)]);

        Assert.NotNull(spread.Package);
        Assert.Equal(PackageKind.FuturesSpread, spread.Package.Kind);
        Assert.Empty(spread.Tags);
        Assert.True(InstrumentContractValidator.Validate(spread).IsValid);
    }

    [Fact]
    public void Validator_AllowsObservableFormulaWithoutExecution()
    {
        var observable = Contracts.Observable("CUSTOM_SIGNAL", "DATA", null, ObservableKind.AlternativeData, "schema:v1");

        var validation = InstrumentContractValidator.Validate(observable);

        Assert.True(validation.IsValid);
        Assert.False(observable.VenueRules.SupportsExecution);
    }

    [Fact]
    public void TradingGrid_ConvertsPricesAndRoundsSize()
    {
        var grid = new TradingGrid(0.05m, 0.1m);

        Assert.Equal(201L, grid.ToTick(new Price(10.05m, Currency.USD)).Ticks);
        Assert.Equal(new Price(10.05m, Currency.USD), grid.FromTick(new TickPrice(201, 0.05m), Currency.USD));
        Assert.Equal(new Qty(1.2m), grid.RoundSize(new Qty(1.24m)));
    }

    [Fact]
    public void TradingGrid_CanUsePriceDependentTickRules()
    {
        var grid = new TradingGrid(
            0.05m,
            1m,
            PriceIncrementRule: new PriceIncrementRule.Piecewise([
                new PriceIncrementBand(null, new Price(3m, Currency.USD), 0.05m),
                new PriceIncrementBand(new Price(3m, Currency.USD), null, 0.10m)
            ]));

        Assert.Equal(0.05m, grid.PriceIncrementFor(new Price(2.95m, Currency.USD)));
        Assert.Equal(0.10m, grid.PriceIncrementFor(new Price(3m, Currency.USD)));
        Assert.Equal(31L, grid.ToTickUsingRule(new Price(3.10m, Currency.USD)).Ticks);
    }

    [Fact]
    public void IndexOptionRecipe_CanCarryVariableTickRule()
    {
        var index = Contracts.Index("SPX", "CBOE", Currency.USD, 0.01m).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var rule = new PriceIncrementRule.Piecewise([
            new PriceIncrementBand(null, new Price(3m, Currency.USD), 0.05m),
            new PriceIncrementBand(new Price(3m, Currency.USD), null, 0.10m)
        ]);

        var contract = Contracts.IndexOption(
            "SPX-C-5000",
            "CBOE",
            index,
            Currency.USD,
            0.05m,
            1m,
            100m,
            new Price(5000m, Currency.USD),
            expiry,
            OptionRight.Call,
            priceIncrementRule: rule);

        Assert.Same(rule, contract.Grid.PriceIncrementRule);
        Assert.Equal(0.10m, contract.Grid.PriceIncrementFor(new Price(5m, Currency.USD)));
    }

    [Fact]
    public void CryptoOptionRecipes_UseTypedExposureInsteadOfProductTags()
    {
        var btc = Contracts.CryptoSpot("BTCUSDT", Venue.Binance, Currency.BTC, Currency.USDT, 0.01m, 0.0001m).Instrument;
        var expiry = Instant.FromUnixSeconds(1_796_016_000);

        var linear = Contracts.LinearCryptoOption("BTC-C-100K", "DERIBIT", btc, Currency.USDT, Currency.USDT, 0.1m, 0.001m, 1m, new Price(100_000m, Currency.USDT), expiry, OptionRight.Call);
        var inverse = Contracts.InverseCryptoOption("BTCUSD-C-100K", "DERIBIT", btc, Currency.BTC, Currency.USD, Currency.BTC, 0.1m, 0.001m, 1m, new Price(100_000m, Currency.USD), expiry, OptionRight.Call);
        var quanto = Contracts.QuantoCryptoOption("BTCUSDT-C-100K-USD", "DERIBIT", btc, Currency.BTC, Currency.USDT, Currency.USD, 1m, 0.1m, 0.001m, 1m, new Price(100_000m, Currency.USDT), expiry, OptionRight.Call);

        Assert.Equal(EconomicExposureKind.Linear, linear.Exposure.Kind());
        Assert.Equal(EconomicExposureKind.Inverse, inverse.Exposure.Kind());
        Assert.Equal(EconomicExposureKind.Quanto, quanto.Exposure.Kind());
        Assert.Empty(linear.Tags);
        Assert.Empty(inverse.Tags);
        Assert.Empty(quanto.Tags);
        Assert.True(InstrumentContractValidator.Validate(linear).IsValid);
        Assert.True(InstrumentContractValidator.Validate(inverse).IsValid);
        Assert.True(InstrumentContractValidator.Validate(quanto).IsValid);
    }
}
