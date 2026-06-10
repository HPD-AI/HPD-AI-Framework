using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public ref struct TimerContext
{
    private readonly MarketKernel _market;
    private readonly Scheduled _scheduled;
    private PortfolioContextFrame _portfolio;

    internal TimerContext(in MarketKernel market, ref PortfolioContext portfolio, Scheduled scheduled)
    {
        _market = market;
        _scheduled = scheduled;
        _portfolio = portfolio.AsFrame();
        Name = scheduled.Name;
    }

    public string Name { get; }
    public Instant Time => _scheduled.Time;
    public StrategyId? StrategyId => _scheduled.StrategyId;

    public decimal GetPositionQty(AssetId id) => _portfolio.GetPositionQty(id);

    public void Buy(AssetId id, Qty quantity, ExecutionSpec execution)
        => _portfolio.Buy(id, quantity, execution);

    public void Sell(AssetId id, Qty quantity, ExecutionSpec execution)
        => _portfolio.Sell(id, quantity, execution);

    public void Cancel(AssetId id, OrderId orderId, string? reason = null)
        => _portfolio.Cancel(id, orderId, reason);

    public void Modify(AssetId id, OrderId orderId, Qty? newQuantity = null, Price? newLimitPrice = null)
        => _portfolio.Modify(id, orderId, newQuantity, newLimitPrice);

    public void Flatten(AssetId id)
    {
        var qty = _portfolio.GetPositionQty(id);
        if (qty == 0m)
            return;

        if (qty > 0m)
            _portfolio.Sell(id, new Qty(qty), Execution.Market());
        else
            _portfolio.Buy(id, new Qty(Math.Abs(qty)), Execution.Market());
    }
}
