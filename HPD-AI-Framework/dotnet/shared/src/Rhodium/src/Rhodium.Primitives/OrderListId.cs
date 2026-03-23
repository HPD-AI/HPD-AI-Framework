namespace Rhodium.Primitives;

/// <summary>
/// Unique identifier for an order list (grouped orders with contingency).
/// </summary>
public readonly record struct OrderListId(string Value)
{
    public static OrderListId New() => new(Guid.NewGuid().ToString("N")[..12]);
    public static implicit operator OrderListId(string v) => new(v);
    public override string ToString() => Value;
}
