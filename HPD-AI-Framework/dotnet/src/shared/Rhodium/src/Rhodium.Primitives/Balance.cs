namespace Rhodium.Primitives;

/// <summary>
/// Unique identifier for orders.
/// </summary>
public readonly record struct OrderId(long Value)
{
    private static long _next;
    public static OrderId New() => new(Interlocked.Increment(ref _next));
    public static implicit operator OrderId(long value) => new(value);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Mutable account state with reservation tracking.
/// Prevents double-spending by locking funds per order.
/// </summary>
public class Balance
{
    public Currency Currency { get; }
    public Money Equity { get; private set; }

    // The Ledger: Tracks reservations per OrderId
    private readonly Dictionary<OrderId, Money> _reservations = new();

    public Balance(Currency currency, Money initialEquity)
    {
        Currency = currency;
        Equity = initialEquity;
    }

    public Money Locked => new(_reservations.Values.Sum(m => m.Amount), Currency);
    public Money Available => new(Equity.Amount - Locked.Amount, Currency);

    public void Reserve(OrderId orderId, Money amount)
    {
        if (amount.Amount > Available.Amount)
            throw new InsufficientFundsException(orderId, amount, Available);
        _reservations[orderId] = amount;
    }

    public void Release(OrderId orderId) => _reservations.Remove(orderId);

    public void ApplyFill(OrderId orderId, Money cost, Money fee)
    {
        _reservations.Remove(orderId);
        Equity = new Money(Equity.Amount - cost.Amount - fee.Amount, Currency);
    }

    public void Credit(Money amount) =>
        Equity = new Money(Equity.Amount + amount.Amount, Currency);
}

public class InsufficientFundsException : Exception
{
    public OrderId OrderId { get; }
    public Money Requested { get; }
    public Money Available { get; }

    public InsufficientFundsException(OrderId orderId, Money requested, Money available)
        : base($"Insufficient funds for order {orderId}: requested {requested}, available {available}")
    {
        OrderId = orderId;
        Requested = requested;
        Available = available;
    }
}
