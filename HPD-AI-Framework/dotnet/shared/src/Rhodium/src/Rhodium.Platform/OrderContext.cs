using Rhodium.Primitives;
using Rhodium.Kernel;

namespace Rhodium.Platform;

public ref struct OrderContext
{
    private PortfolioContextFrame _portfolio;

    internal OrderContext(
        StrategyId strategyId,
        OrderId orderId,
        OrderStatus status,
        int variantId,
        ref PortfolioContext portfolio,
        AssetId? assetId = null,
        string? reason = null)
    {
        StrategyId = strategyId;
        OrderId = orderId;
        Status = status;
        VariantId = variantId;
        AssetId = assetId;
        Reason = reason;
        _portfolio = portfolio.AsFrame();
    }

    public StrategyId StrategyId { get; }
    public OrderId OrderId { get; }
    public OrderStatus Status { get; }
    public int VariantId { get; }
    public AssetId? AssetId { get; }
    public string? Reason { get; }

    public void Cancel(string? reason = null)
    {
        if (!AssetId.HasValue)
            throw new InvalidOperationException("Order context does not carry an asset id. Use Cancel(AssetId, ...) instead.");

        _portfolio.Cancel(AssetId.Value, OrderId, reason);
    }

    public void Cancel(AssetId assetId, string? reason = null)
        => _portfolio.Cancel(assetId, OrderId, reason);

    public void Modify(Qty? newQuantity = null, Price? newLimitPrice = null)
    {
        if (!AssetId.HasValue)
            throw new InvalidOperationException("Order context does not carry an asset id. Use Modify(AssetId, ...) instead.");

        _portfolio.Modify(AssetId.Value, OrderId, newQuantity, newLimitPrice);
    }

    public void Modify(AssetId assetId, Qty? newQuantity = null, Price? newLimitPrice = null)
        => _portfolio.Modify(assetId, OrderId, newQuantity, newLimitPrice);
}
