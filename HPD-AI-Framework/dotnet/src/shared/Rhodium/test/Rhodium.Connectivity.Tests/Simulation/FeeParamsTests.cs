using Rhodium.Connectivity.Simulation;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests.Simulation;

/// <summary>
/// Tests for FeeParams and all 5 fee models.
/// </summary>
public class FeeParamsTests
{
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
    public void AllFeeModelTypes_AreDefined()
    {
        var allModels = Enum.GetValues<FeeModelType>();

        Assert.Equal(5, allModels.Length);
        Assert.Contains(FeeModelType.PercentageOfValue, allModels);
        Assert.Contains(FeeModelType.PerQuantity, allModels);
        Assert.Contains(FeeModelType.PerTrade, allModels);
        Assert.Contains(FeeModelType.TieredByVolume, allModels);
        Assert.Contains(FeeModelType.Directional, allModels);
    }
}
