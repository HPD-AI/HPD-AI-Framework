using Rhodium.Primitives;

namespace Rhodium.Platform;

public readonly ref struct OrderContext
{
    internal OrderContext(
        StrategyId strategyId,
        OrderId orderId,
        OrderStatus status,
        int variantId,
        string? reason = null)
    {
        StrategyId = strategyId;
        OrderId = orderId;
        Status = status;
        VariantId = variantId;
        Reason = reason;
    }

    public StrategyId StrategyId { get; }
    public OrderId OrderId { get; }
    public OrderStatus Status { get; }
    public int VariantId { get; }
    public string? Reason { get; }
}
