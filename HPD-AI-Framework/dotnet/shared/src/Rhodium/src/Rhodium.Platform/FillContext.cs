using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public ref struct FillContext
{
    private PortfolioContextFrame _portfolio;

    internal FillContext(
        StrategyId strategyId,
        OrderId orderId,
        AssetId assetId,
        Side side,
        Qty filledQty,
        Price fillPrice,
        Money commission,
        PositionState position,
        ref PortfolioContext portfolio)
    {
        StrategyId = strategyId;
        OrderId = orderId;
        AssetId = assetId;
        Side = side;
        FilledQty = filledQty;
        FillPrice = fillPrice;
        Commission = commission;
        Position = position;
        _portfolio = portfolio.AsFrame();
    }

    public StrategyId StrategyId { get; }
    public OrderId OrderId { get; }
    public AssetId AssetId { get; }
    public Side Side { get; }
    public Qty FilledQty { get; }
    public Price FillPrice { get; }
    public Money Commission { get; }
    public PositionState Position { get; }

    public void Buy(Qty quantity, ExecutionSpec execution)
        => _portfolio.Buy(AssetId, quantity, execution);

    public void Sell(Qty quantity, ExecutionSpec execution)
        => _portfolio.Sell(AssetId, quantity, execution);

    public void Cancel(string? reason = null)
        => _portfolio.Cancel(AssetId, OrderId, reason);

    public void Cancel(OrderId orderId, string? reason = null)
        => _portfolio.Cancel(AssetId, orderId, reason);

    public void Cancel(AssetId assetId, OrderId orderId, string? reason = null)
        => _portfolio.Cancel(assetId, orderId, reason);

    public void Modify(Qty? newQuantity = null, Price? newLimitPrice = null)
        => _portfolio.Modify(AssetId, OrderId, newQuantity, newLimitPrice);

    public void Modify(OrderId orderId, Qty? newQuantity = null, Price? newLimitPrice = null)
        => _portfolio.Modify(AssetId, orderId, newQuantity, newLimitPrice);

    public void Modify(AssetId assetId, OrderId orderId, Qty? newQuantity = null, Price? newLimitPrice = null)
        => _portfolio.Modify(assetId, orderId, newQuantity, newLimitPrice);
}
