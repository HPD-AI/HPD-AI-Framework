using Rhodium.Connectivity.Simulation;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests.Simulation;

/// <summary>
/// Tests for SimulationConfig and all presets.
/// </summary>
public class SimulationConfigTests
{
    [Fact]
    public void CryptoFuturesRealistic_HasCorrectSettings()
    {
        var config = SimulationConfig.CryptoFuturesRealistic();

        Assert.Equal(Duration.FromMicros(500), config.Latency.EntryMean);
        Assert.Equal(Duration.FromMicros(500), config.Latency.ResponseMean);
        Assert.Equal(0.2, config.Latency.StdDevFraction);

        Assert.Equal(QueueModelType.PowerProbabilistic, config.Queue.Model);
        Assert.True(config.Queue.ProbabilisticEntry);

        Assert.Equal(2m, config.Fees.MakerBps);
        Assert.Equal(4m, config.Fees.TakerBps);

        Assert.Equal(SlippageModelType.None, config.Slippage.Model);
        Assert.Equal(FillBehavior.PartialFillOnTrade, config.FillBehavior);
    }

    [Fact]
    public void Conservative_HasHigherFeesAndLatency()
    {
        var config = SimulationConfig.Conservative();

        Assert.Equal(Duration.FromMillis(10), config.Latency.EntryMean);
        Assert.Equal(Duration.FromMillis(10), config.Latency.ResponseMean);

        Assert.Equal(QueueModelType.RiskAverse, config.Queue.Model);

        Assert.Equal(5m, config.Fees.MakerBps);
        Assert.Equal(15m, config.Fees.TakerBps);

        Assert.Equal(SlippageModelType.VolumeProportional, config.Slippage.Model);
        Assert.Equal(1m, config.Slippage.BpsPerLotSize);
        Assert.Equal(FillBehavior.NoPartialFill, config.FillBehavior);
    }

    [Fact]
    public void IlliquidMarket_UsesCubicQueueProfile()
    {
        var config = SimulationConfig.IlliquidMarket();

        Assert.Equal(QueueModelType.PowerProbabilistic, config.Queue.Model);
        Assert.Equal(3.0, config.Queue.Alpha);
        Assert.False(config.Queue.ProbabilisticEntry);

        Assert.Equal(10m, config.Fees.MakerBps);
        Assert.Equal(20m, config.Fees.TakerBps);

        Assert.Equal(2m, config.Slippage.BpsPerLotSize);
        Assert.Equal(FillBehavior.PartialFillOnTrade, config.FillBehavior);
    }

    [Fact]
    public void USEquities_UsesFixedFees()
    {
        var config = SimulationConfig.USEquities();

        Assert.Equal(Duration.FromMicros(100), config.Latency.EntryMean);

        Assert.Equal(QueueModelType.PowerProbabilistic, config.Queue.Model);
        Assert.Equal(2.0, config.Queue.Alpha);

        Assert.Equal(FeeModelType.PerTrade, config.Fees.Model);
        Assert.Equal(0.50m, config.Fees.FixedFee.Amount);

        Assert.Equal(SlippageModelType.None, config.Slippage.Model);
    }

    [Fact]
    public void Instant_HasZeroLatencyAndFees()
    {
        var config = SimulationConfig.Instant();

        Assert.Equal(Duration.Zero, config.Latency.EntryMean);
        Assert.Equal(Duration.Zero, config.Latency.ResponseMean);

        Assert.Equal(QueueModelType.AlwaysFront, config.Queue.Model);

        Assert.Equal(0m, config.Fees.MakerBps);
        Assert.Equal(0m, config.Fees.TakerBps);

        Assert.Equal(SlippageModelType.None, config.Slippage.Model);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new SimulationConfig
        {
            Latency = new(Duration.FromMillis(1), Duration.FromMillis(1)),
            Queue = QueueParams.PowerQuadratic(),
            Fees = FeeParams.Zero
        };

        Assert.Equal(FillBehavior.NoPartialFill, config.FillBehavior);
        Assert.Equal(DepthLevel.L2_MarketByPrice, config.RequiredDepth);
        Assert.Equal(AccountType.Cash, config.AccountType);
        Assert.Equal(MarketStatus.Open, config.InitialMarketStatus);
        Assert.Equal(BarOrderingMode.Fixed, config.BarOrdering);
        Assert.IsType<DefaultFillModel>(config.FillModel);
        Assert.Equal(0, config.Seed);
    }

    [Fact]
    public void CustomConfig_AllowsOverrides()
    {
        var config = new SimulationConfig
        {
            Latency = new(Duration.FromMicros(200), Duration.FromMicros(300), 0.15),
            Queue = QueueParams.Logarithmic(scale: 20.0),
            Fees = FeeParams.Directional(buyBps: 5m, sellBps: 7m),
            Slippage = new(SlippageModelType.VolumeProportional, BpsPerLotSize: 0.5m),
            FillBehavior = FillBehavior.PartialFillOnTrade,
            RequiredDepth = DepthLevel.L1_TopOfBook,
            AccountType = AccountType.Margin,
            InitialMarketStatus = MarketStatus.PreOpen,
            BarOrdering = BarOrderingMode.Adaptive,
            FillModel = new SizeAwareFillModel(baseQty: 20m, slippageBpsPerLot: 1m),
            Seed = 12345
        };

        Assert.Equal(0.15, config.Latency.StdDevFraction);
        Assert.Equal(20.0, config.Queue.Scale);
        Assert.Equal(5m, config.Fees.BuyFeeBps);
        Assert.Equal(0.5m, config.Slippage.BpsPerLotSize);
        Assert.Equal(FillBehavior.PartialFillOnTrade, config.FillBehavior);
        Assert.Equal(DepthLevel.L1_TopOfBook, config.RequiredDepth);
        Assert.Equal(AccountType.Margin, config.AccountType);
        Assert.Equal(MarketStatus.PreOpen, config.InitialMarketStatus);
        Assert.Equal(BarOrderingMode.Adaptive, config.BarOrdering);
        Assert.IsType<SizeAwareFillModel>(config.FillModel);
        Assert.Equal(12345, config.Seed);
    }

    [Fact]
    public void AllDepthLevels_AreDefined()
    {
        var levels = Enum.GetValues<DepthLevel>();

        Assert.Equal(2, levels.Length);
        Assert.Contains(DepthLevel.L1_TopOfBook, levels);
        Assert.Contains(DepthLevel.L2_MarketByPrice, levels);
    }

    [Fact]
    public void AllAccountTypes_AreDefined()
    {
        var types = Enum.GetValues<AccountType>();

        Assert.Equal(2, types.Length);
        Assert.Contains(AccountType.Cash, types);
        Assert.Contains(AccountType.Margin, types);
    }

    [Fact]
    public void AllMarketStatuses_AreDefined()
    {
        var statuses = Enum.GetValues<MarketStatus>();

        Assert.Equal(4, statuses.Length);
        Assert.Contains(MarketStatus.PreOpen, statuses);
        Assert.Contains(MarketStatus.Open, statuses);
        Assert.Contains(MarketStatus.Closed, statuses);
        Assert.Contains(MarketStatus.Halted, statuses);
    }

    [Fact]
    public void AllBarOrderingModes_AreDefined()
    {
        var modes = Enum.GetValues<BarOrderingMode>();

        Assert.Equal(2, modes.Length);
        Assert.Contains(BarOrderingMode.Fixed, modes);
        Assert.Contains(BarOrderingMode.Adaptive, modes);
    }

    [Fact]
    public void AllFillBehaviors_AreDefined()
    {
        var behaviors = Enum.GetValues<FillBehavior>();

        Assert.Equal(2, behaviors.Length);
        Assert.Contains(FillBehavior.NoPartialFill, behaviors);
        Assert.Contains(FillBehavior.PartialFillOnTrade, behaviors);
    }
}
