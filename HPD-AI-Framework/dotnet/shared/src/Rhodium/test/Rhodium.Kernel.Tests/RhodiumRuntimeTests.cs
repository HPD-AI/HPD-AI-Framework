using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel.Tests;

public class RhodiumRuntimeTests
{
    [Fact]
    public void Runtime_Constructor_InitializesCoreOwners()
    {
        using var runtime = new RhodiumRuntime();

        Assert.NotNull(runtime.Tensors);
        Assert.NotNull(runtime.MarketState);
        Assert.NotNull(runtime.BatchMap);
        Assert.NotNull(runtime.MarketBatchMap);
        Assert.NotNull(runtime.WorldState);
    }

    [Fact]
    public void MarketKernel_ReadsContractProjectionFromRuntime()
    {
        using var runtime = new RhodiumRuntime();
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var contract = Contracts.OptionContract(
            "SPY-C-500",
            "OPRA",
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            new Price(500m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call,
            ExerciseStyle.American,
            unitOfTrade: 100m);

        runtime.SetContract(0, contract);
        var market = runtime.CreateMarketKernel();
        var assetId = new AssetId(0);

        Assert.Equal(contract.Instrument, market.GetContract(assetId).Instrument);
        Assert.Equal(0.05m, market.GetPriceIncrement(assetId));
        Assert.Equal(1m, market.GetSizeIncrement(assetId));
        Assert.Equal(1m, market.GetLotSize(assetId));
        Assert.Equal(100m, market.GetMultiplier(assetId));
        Assert.Equal(100m, market.GetContractUnitOfTrade(assetId));
        Assert.Equal(EconomicExposureKind.Linear, market.GetExposureKind(assetId));
        Assert.Equal(Currency.USD, market.GetQuoteCurrency(assetId));
        Assert.Equal(Currency.USD, market.GetSettlementCurrency(assetId));
        Assert.True(market.IsTradable(assetId));
        Assert.True(market.SupportsExecution(assetId));
        Assert.True(market.IsOption(assetId));
        Assert.False(market.IsPackage(assetId));
        Assert.Equal(contract.VenueRules.AllowedOrderTypes, market.GetAllowedOrderTypes(assetId));
        Assert.Equal(contract.VenueRules.AllowedTimeInForce, market.GetAllowedTimeInForce(assetId));
        Assert.Equal(0.05m, market.GetGrid(assetId).PriceIncrement);
    }

    [Fact]
    public void MarketKernel_ReadsDepthFromRuntime()
    {
        using var runtime = new RhodiumRuntime();
        var depth = new HashMapDepth(0.01m, 1m);

        depth.Update(Side.Buy, 10000, 100m, Instant.Now);
        depth.Update(Side.Sell, 10001, 50m, Instant.Now);
        runtime.SetDepth(0, depth);

        var market = runtime.CreateMarketKernel();

        Assert.Equal(10000L, market.GetBestBidTick(new AssetId(0)));
        Assert.Equal(10001L, market.GetBestAskTick(new AssetId(0)));
        Assert.Equal(100m, market.GetQtyAtTick(new AssetId(0), Side.Buy, 10000));
    }

    [Fact]
    public void WorldState_RoutesPositionsByStrategyId()
    {
        using var runtime = new RhodiumRuntime();
        var a = new StrategyId(1);
        var b = new StrategyId(2);

        runtime.WorldState.PositionAt(a, 0).Quantity = 10m;
        runtime.WorldState.PositionAt(b, 0).Quantity = 20m;

        Assert.Equal(10m, runtime.WorldState.PositionAt(a, 0).Quantity);
        Assert.Equal(20m, runtime.WorldState.PositionAt(b, 0).Quantity);
    }

    [Fact]
    public void Runtime_BuildSnapshot_UsesInstrumentContractsForExposure()
    {
        using var runtime = new RhodiumRuntime();
        var strategyId = new StrategyId(1);
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
        runtime.SetContract(0, contract);
        runtime.SetTime(Instant.FromUnixSeconds(100));

        runtime.WorldState.PositionAt(strategyId, 0).ApplyFill(
            contract,
            Side.Buy,
            new Qty(2m),
            new Price(5000m, Currency.USD),
            Money.USD(0m));

        var snapshot = runtime.BuildSnapshot(
            strategyId,
            universeSize: 1,
            new Dictionary<int, Price>
            {
                [0] = new Price(5010m, Currency.USD)
            });

        Assert.Equal(501000m, snapshot.GrossExposure);
        Assert.Equal(501000m, snapshot.NetExposure);
        Assert.Equal(1, snapshot.GetPositions().Length);
        Assert.Equal(contract.Instrument, snapshot.GetPositions()[0].Instrument);
    }

    [Fact]
    public void MarketKernel_CanReadTensorScalar()
    {
        using var runtime = new RhodiumRuntime();
        runtime.Tensors.Grow();
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(101.25);

        var market = runtime.CreateMarketKernel();

        Assert.Equal(101.25, market.GetScalar(Field.Close, new AssetId(0)));
    }
}
