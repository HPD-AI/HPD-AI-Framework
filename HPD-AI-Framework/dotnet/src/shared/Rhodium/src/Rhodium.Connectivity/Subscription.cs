using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Market data subscription request.
/// </summary>
public readonly record struct Subscription(
    Instrument Instrument,
    SubscriptionType Type,
    SubscriptionDepth Depth = SubscriptionDepth.Top);

/// <summary>
/// Type of market data to subscribe to.
/// </summary>
public enum SubscriptionType : byte
{
    /// <summary>Trade prints (last price, volume).</summary>
    Trades,

    /// <summary>Quote updates (bid/ask).</summary>
    Quotes,

    /// <summary>Order book depth (L2/L3).</summary>
    Depth,

    /// <summary>Aggregated bars (OHLCV).</summary>
    Bars
}

/// <summary>
/// Depth of order book subscription.
/// </summary>
public enum SubscriptionDepth : byte
{
    /// <summary>Top of book only (best bid/ask).</summary>
    Top = 1,

    /// <summary>Top 5 levels.</summary>
    L2_5 = 5,

    /// <summary>Top 10 levels.</summary>
    L2_10 = 10,

    /// <summary>Top 20 levels.</summary>
    L2_20 = 20,

    /// <summary>Full book (L3 / Market by Order).</summary>
    Full = 255
}
