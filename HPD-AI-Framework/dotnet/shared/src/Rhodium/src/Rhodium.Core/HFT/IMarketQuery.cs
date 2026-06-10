namespace Rhodium.HFT;

/// <summary>
/// Query interface for cross-space access (strategy → market).
/// Allows strategies to query L3 market data without direct tensor access.
/// </summary>
public interface IMarketQuery
{
    /// <summary>
    /// Get all orders at a specific price level.
    /// </summary>
    IEnumerable<L3Order> GetOrdersAtLevel(string instrumentId, int priceLevel);

    /// <summary>
    /// Get total size at a price level.
    /// </summary>
    double GetSizeAtLevel(string instrumentId, int priceLevel);

    /// <summary>
    /// Get FIFO queue depth at a price level.
    /// </summary>
    int GetQueueDepth(string instrumentId, int priceLevel);

    /// <summary>
    /// Get estimated queue position for a hypothetical order.
    /// </summary>
    int EstimateQueuePosition(string instrumentId, double price, long yourOrderTimestamp);
}

/// <summary>
/// L3 order representation.
/// </summary>
public readonly record struct L3Order(
    long OrderId,
    double Price,
    double Size,
    long Timestamp,
    OrderSide Side);

/// <summary>
/// Order side for L3 orders.
/// </summary>
public enum OrderSide
{
    Buy,
    Sell
}
