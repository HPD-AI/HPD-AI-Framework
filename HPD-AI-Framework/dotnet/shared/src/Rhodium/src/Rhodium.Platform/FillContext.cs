using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public readonly ref struct FillContext
{
    internal FillContext(
        StrategyId strategyId,
        OrderId orderId,
        AssetId assetId,
        Side side,
        Qty filledQty,
        Price fillPrice,
        Money commission,
        PositionState position)
    {
        StrategyId = strategyId;
        OrderId = orderId;
        AssetId = assetId;
        Side = side;
        FilledQty = filledQty;
        FillPrice = fillPrice;
        Commission = commission;
        Position = position;
    }

    public StrategyId StrategyId { get; }
    public OrderId OrderId { get; }
    public AssetId AssetId { get; }
    public Side Side { get; }
    public Qty FilledQty { get; }
    public Price FillPrice { get; }
    public Money Commission { get; }
    public PositionState Position { get; }
}
