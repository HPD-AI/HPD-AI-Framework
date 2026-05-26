using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

public static class StrategyTest
{
    public static StrategyTestBuilder<TStrategy> For<TStrategy>()
        where TStrategy : Strategy, new()
        => new();
}

public sealed class StrategyTestBuilder<TStrategy>
    where TStrategy : Strategy, new()
{
    private readonly List<double> _closeSeries = [];

    public StrategyTestBuilder<TStrategy> WithCloseSeries(params double[] closes)
    {
        _closeSeries.Clear();
        _closeSeries.AddRange(closes);
        return this;
    }

    public StrategyTestResult Run()
    {
        var runtime = new RhodiumRuntime();
        var strategy = new TStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[32];
        var portfolio = runtime.WorldState.BuildContext(
            strategy.Id,
            null,
            default,
            new int[PortfolioContext.CounterCount],
            commands,
            orderIntents: orderIntents);

        foreach (var close in _closeSeries)
        {
            for (var i = 0; i < runtime.BatchMap.TotalSize; i++)
                runtime.Tensors.GetScalar(Field.Close, i) = new PriceF64(close);

            strategy.RunBarGuarded(in market, ref portfolio);
            foreach (var intent in portfolio.DrainOrderIntents())
                ApplyTestFill(runtime, strategy.Id, intent.AssetId, intent.Side, intent.Quantity, in market);
        }

        var snapshot = runtime.WorldState.BuildSnapshot(strategy.Id, runtime.BatchMap.TotalSize, runtime.CurrentTime);
        return new StrategyTestResult(runtime, strategy.Id, snapshot);
    }

    private static void ApplyTestFill(
        RhodiumRuntime runtime,
        StrategyId strategyId,
        AssetId assetId,
        Side side,
        Qty quantity,
        in MarketKernel market)
    {
        var price = new Price((decimal)market.GetScalar(Field.Close, assetId), market.GetQuoteCurrency(assetId));
        runtime.WorldState.PositionAt(strategyId, assetId.VirtualIndex)
            .ApplyFill(market.GetContract(assetId), side, quantity, price, Money.Zero(price.Currency));
    }
}

public sealed class StrategyTestResult : IDisposable
{
    private readonly RhodiumRuntime _runtime;

    internal StrategyTestResult(
        RhodiumRuntime runtime,
        StrategyId strategyId,
        PortfolioSnapshot snapshot)
    {
        _runtime = runtime;
        StrategyId = strategyId;
        Snapshot = snapshot;
    }

    public StrategyId StrategyId { get; }
    public PortfolioSnapshot Snapshot { get; }

    public decimal PositionQuantity(AssetId assetId)
        => _runtime.WorldState.PositionAt(StrategyId, assetId.VirtualIndex).Quantity;

    public void Dispose()
        => _runtime.Dispose();
}
