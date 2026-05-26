using Rhodium.Simulation;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for FeeParams and all fee models.
/// </summary>
public class FeeParamsTests
{
    private static readonly InstrumentContract SpyContract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);

    [Fact]
    public void Zero_CreatesZeroFeeModel()
    {
        var fees = FeeParams.Zero;

        Assert.Equal(FeeModelType.PercentageOfValue, fees.Model);
        Assert.Equal(0m, fees.MakerBps);
        Assert.Equal(0m, fees.TakerBps);
    }

    [Fact]
    public void MakerTaker_CreatesPercentageModel()
    {
        var fees = FeeParams.MakerTaker(makerBps: 5m, takerBps: 10m);

        Assert.Equal(FeeModelType.PercentageOfValue, fees.Model);
        Assert.Equal(5m, fees.MakerBps);
        Assert.Equal(10m, fees.TakerBps);
    }

    [Fact]
    public void PerLot_CreatesPerQuantityModel()
    {
        var makerFee = new Money(0.01m, Currency.USD);
        var takerFee = new Money(0.02m, Currency.USD);

        var fees = FeeParams.PerLot(makerFee, takerFee);

        Assert.Equal(FeeModelType.PerQuantity, fees.Model);
        Assert.Equal(makerFee, fees.MakerFeePerLot);
        Assert.Equal(takerFee, fees.TakerFeePerLot);
    }

    [Fact]
    public void Fixed_CreatesPerTradeModel()
    {
        var fixedFee = new Money(0.50m, Currency.USD);

        var fees = FeeParams.Fixed(fixedFee);

        Assert.Equal(FeeModelType.PerTrade, fees.Model);
        Assert.Equal(fixedFee, fees.FixedFee);
    }

    [Fact]
    public void Directional_CreatesDifferentBuySellFees()
    {
        var fees = FeeParams.Directional(buyBps: 8m, sellBps: 12m);

        Assert.Equal(FeeModelType.Directional, fees.Model);
        Assert.Equal(8m, fees.BuyFeeBps);
        Assert.Equal(12m, fees.SellFeeBps);
    }

    [Fact]
    public void ContractTerms_CreatesContractFeeModel()
    {
        var fees = FeeParams.ContractTerms;

        Assert.Equal(FeeModelType.ContractTerms, fees.Model);
    }

    [Fact]
    public void Calculate_DirectionalUsesOrderSide()
    {
        var fees = FeeParams.Directional(buyBps: 8m, sellBps: 12m);
        var quantity = new Qty(10m);
        var price = new Price(100m, Currency.USD);

        var buyFee = fees.Calculate(SpyContract, quantity, price, Side.Buy, isMaker: false, thirtyDayVolume: Money.USD(0m));
        var sellFee = fees.Calculate(SpyContract, quantity, price, Side.Sell, isMaker: false, thirtyDayVolume: Money.USD(0m));

        Assert.Equal(Money.USD(0.80m), buyFee);
        Assert.Equal(Money.USD(1.20m), sellFee);
    }

    [Fact]
    public void Calculate_AllowsNegativeMakerFeeAsRebate()
    {
        var fees = FeeParams.MakerTaker(makerBps: -1m, takerBps: 4m);
        var quantity = new Qty(10m);
        var price = new Price(100m, Currency.USD);

        var makerRebate = fees.Calculate(SpyContract, quantity, price, Side.Sell, isMaker: true, thirtyDayVolume: Money.USD(0m));
        var takerFee = fees.Calculate(SpyContract, quantity, price, Side.Buy, isMaker: false, thirtyDayVolume: Money.USD(0m));

        Assert.Equal(Money.USD(-0.10m), makerRebate);
        Assert.Equal(Money.USD(0.40m), takerFee);
    }

    [Fact]
    public void BinanceFutures_UsesCorrectFees()
    {
        var fees = FeeParams.BinanceFutures();

        Assert.Equal(FeeModelType.PercentageOfValue, fees.Model);
        Assert.Equal(2m, fees.MakerBps);
        Assert.Equal(4m, fees.TakerBps);
    }

    [Fact]
    public void CoinbaseAdvanced_UsesCorrectFees()
    {
        var fees = FeeParams.CoinbaseAdvanced();

        Assert.Equal(FeeModelType.PercentageOfValue, fees.Model);
        Assert.Equal(40m, fees.MakerBps);
        Assert.Equal(60m, fees.TakerBps);
    }

    [Fact]
    public void InteractiveBrokers_UsesPerQuantityModel()
    {
        var fees = FeeParams.InteractiveBrokers();

        Assert.Equal(FeeModelType.PerQuantity, fees.Model);
        Assert.Equal(0.005m, fees.MakerFeePerLot.Amount);
        Assert.Equal(0.005m, fees.TakerFeePerLot.Amount);
    }

    [Fact]
    public void TieredFeeSchedule_CalculatesCorrectTier()
    {
        var schedule = TieredFeeSchedule.BinanceFuturesVIP();

        // Low volume - tier 0
        var (maker1, taker1) = schedule.GetFeeRate(new Money(1_000_000, Currency.USD));
        Assert.Equal(2m, maker1);
        Assert.Equal(4m, taker1);

        // Mid volume - tier 2
        var (maker2, taker2) = schedule.GetFeeRate(new Money(50_000_000, Currency.USD));
        Assert.Equal(1.4m, maker2);
        Assert.Equal(3.4m, taker2);

        // High volume - tier 3
        var (maker3, taker3) = schedule.GetFeeRate(new Money(100_000_000, Currency.USD));
        Assert.Equal(1.2m, maker3);
        Assert.Equal(3.2m, taker3);
    }

    [Fact]
    public void Tiered_CreatesTieredVolumeModel()
    {
        var schedule = TieredFeeSchedule.BinanceFuturesVIP();

        var fees = FeeParams.Tiered(schedule);

        Assert.Equal(FeeModelType.TieredByVolume, fees.Model);
        Assert.Same(schedule, fees.TieredSchedule);
    }

    [Fact]
    public void Calculate_TieredByVolumeUsesRollingVolumeAndMakerTakerSide()
    {
        var fees = FeeParams.Tiered(new TieredFeeSchedule
        {
            Tiers =
            [
                new FeeTier(Money.USD(0m), MakerBps: 2m, TakerBps: 10m),
                new FeeTier(Money.USD(1_000m), MakerBps: -1m, TakerBps: 1m)
            ]
        });
        var quantity = new Qty(10m);
        var price = new Price(100m, Currency.USD);

        var firstTaker = fees.Calculate(SpyContract, quantity, price, Side.Buy, isMaker: false, thirtyDayVolume: Money.USD(0m));
        var laterTaker = fees.Calculate(SpyContract, quantity, price, Side.Buy, isMaker: false, thirtyDayVolume: Money.USD(1_000m));
        var laterMaker = fees.Calculate(SpyContract, quantity, price, Side.Sell, isMaker: true, thirtyDayVolume: Money.USD(1_000m));

        Assert.Equal(Money.USD(1m), firstTaker);
        Assert.Equal(Money.USD(0.10m), laterTaker);
        Assert.Equal(Money.USD(-0.10m), laterMaker);
    }

    [Fact]
    public void Calculate_PercentageUsesContractNotional()
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
        var fees = FeeParams.MakerTaker(makerBps: 0m, takerBps: 1m);

        var fee = fees.Calculate(
            contract,
            new Qty(2m),
            new Price(5000m, Currency.USD),
            Side.Buy,
            isMaker: false,
            thirtyDayVolume: Money.USD(0m));

        Assert.Equal(Money.USD(50m), fee);
    }

    [Fact]
    public void Calculate_ContractTermsUsesInstrumentFeeTerms()
    {
        var contract = Contracts.CryptoSpot(
            "BTCUSD",
            Venue.Binance,
            Currency.BTC,
            Currency.USD,
            tick: 0.01m,
            lot: 0.0001m);

        var makerFee = FeeParams.ContractTerms.Calculate(
            contract,
            new Qty(2m),
            new Price(50_000m, Currency.USD),
            Side.Sell,
            isMaker: true,
            thirtyDayVolume: Money.USD(0m));
        var takerFee = FeeParams.ContractTerms.Calculate(
            contract,
            new Qty(2m),
            new Price(50_000m, Currency.USD),
            Side.Buy,
            isMaker: false,
            thirtyDayVolume: Money.USD(0m));

        Assert.Equal(Money.USD(20m), makerFee);
        Assert.Equal(Money.USD(40m), takerFee);
    }

    [Fact]
    public void AllFeeModelTypes_AreDefined()
    {
        var allModels = Enum.GetValues<FeeModelType>();

        Assert.Equal(6, allModels.Length);
        Assert.Contains(FeeModelType.PercentageOfValue, allModels);
        Assert.Contains(FeeModelType.PerQuantity, allModels);
        Assert.Contains(FeeModelType.PerTrade, allModels);
        Assert.Contains(FeeModelType.TieredByVolume, allModels);
        Assert.Contains(FeeModelType.Directional, allModels);
        Assert.Contains(FeeModelType.ContractTerms, allModels);
    }
}
