namespace Rhodium.Primitives;

/// <summary>
/// A group of related orders with contingency behavior.
///
/// Examples:
/// - Bracket order: Entry (OTO) → [Stop-loss (OCO) Take-profit]
/// - OCO pair: Stop-loss and take-profit, first fill cancels other
/// </summary>
public sealed class OrderList
{
    public OrderListId Id { get; }
    public ContingencyType Contingency { get; }
    public IReadOnlyList<Order> Orders { get; }
    public Instrument Instrument { get; }

    public OrderList(
        OrderListId id,
        ContingencyType contingency,
        IReadOnlyList<Order> orders,
        Instrument instrument)
    {
        if (orders.Count < 2)
            throw new ArgumentException("OrderList requires at least 2 orders", nameof(orders));

        if (orders.Any(o => o.Instrument != instrument))
            throw new ArgumentException("All orders must be for same instrument", nameof(orders));

        Id = id;
        Contingency = contingency;
        Orders = orders;
        Instrument = instrument;
    }

    /// <summary>Create a new order list with auto-generated ID.</summary>
    public static OrderList Create(
        ContingencyType contingency,
        Instrument instrument,
        params Order[] orders)
        => new(OrderListId.New(), contingency, orders, instrument);

    /// <summary>Create an OCO pair (common case).</summary>
    public static OrderList CreateOCO(Instrument instrument, Order order1, Order order2) =>
        new(OrderListId.New(), ContingencyType.OCO, new[] { order1, order2 }, instrument);

    /// <summary>Create an OTO chain (entry triggers others).</summary>
    public static OrderList CreateOTO(Instrument instrument, Order entry, params Order[] triggered) =>
        new(OrderListId.New(), ContingencyType.OTO, new[] { entry }.Concat(triggered).ToArray(), instrument);

    /// <summary>Create a bracket order (entry + stop-loss + take-profit).</summary>
    public static OrderList CreateBracket(
        Instrument instrument,
        Order entry,
        Order stopLoss,
        Order takeProfit)
    {
        return new(OrderListId.New(), ContingencyType.OTO, new[] { entry, stopLoss, takeProfit }, instrument);
    }
}
