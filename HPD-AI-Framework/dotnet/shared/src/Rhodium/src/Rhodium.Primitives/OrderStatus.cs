namespace Rhodium.Primitives;

/// <summary>
/// Order status.
/// </summary>
public enum OrderStatus : byte
{
    Pending = 0,        // Submitted, not yet acknowledged
    Open = 1,           // Acknowledged, working
    PartiallyFilled = 2,
    Filled = 3,
    Cancelled = 4,
    Rejected = 5,
    Expired = 6
}
