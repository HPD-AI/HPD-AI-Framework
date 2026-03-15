namespace Rhodium.Primitives;

/// <summary>
/// A quantity of something. Dimensionless number.
/// </summary>
public readonly record struct Qty(decimal Value) : IComparable<Qty>
{
    public static readonly Qty Zero = new(0m);

    public bool IsZero => Value == 0m;
    public bool IsPositive => Value > 0m;
    public bool IsNegative => Value < 0m;
    public Qty Abs => new(Math.Abs(Value));
    public Qty Negate => new(-Value);

    public int CompareTo(Qty other) => Value.CompareTo(other.Value);

    public static Qty operator +(Qty a, Qty b) => new(a.Value + b.Value);
    public static Qty operator -(Qty a, Qty b) => new(a.Value - b.Value);
    public static Qty operator *(Qty a, decimal b) => new(a.Value * b);
    public static Qty operator /(Qty a, decimal b) => new(a.Value / b);
    public static Qty operator -(Qty a) => new(-a.Value);

    public static bool operator >(Qty a, Qty b) => a.Value > b.Value;
    public static bool operator <(Qty a, Qty b) => a.Value < b.Value;
    public static bool operator >=(Qty a, Qty b) => a.Value >= b.Value;
    public static bool operator <=(Qty a, Qty b) => a.Value <= b.Value;

    public static implicit operator Qty(decimal value) => new(value);
    public static implicit operator Qty(int value) => new(value);
    public static implicit operator decimal(Qty qty) => qty.Value;

    public override string ToString() => Value.ToString("G");
}

/// <summary>
/// A price in a specific currency.
/// </summary>
public readonly record struct Price(decimal Value, Currency Currency = default) : IComparable<Price>
{
    public static readonly Price Zero = new(0m);

    public bool IsZero => Value == 0m;
    public bool IsPositive => Value > 0m;

    public int CompareTo(Price other) => Value.CompareTo(other.Value);

    public static Price operator +(Price a, Price b) => new(a.Value + b.Value, a.Currency);
    public static Price operator -(Price a, Price b) => new(a.Value - b.Value, a.Currency);
    public static Price operator *(Price a, decimal b) => new(a.Value * b, a.Currency);
    public static Price operator /(Price a, decimal b) => new(a.Value / b, a.Currency);

    public static bool operator >(Price a, Price b) => a.Value > b.Value;
    public static bool operator <(Price a, Price b) => a.Value < b.Value;
    public static bool operator >=(Price a, Price b) => a.Value >= b.Value;
    public static bool operator <=(Price a, Price b) => a.Value <= b.Value;

    public static implicit operator Price(decimal value) => new(value);
    public static implicit operator decimal(Price price) => price.Value;

    public static Price Max(Price a, Price b) => a.Value >= b.Value ? a : b;
    public static Price Min(Price a, Price b) => a.Value <= b.Value ? a : b;

    public override string ToString() => Currency == default
        ? Value.ToString("F2")
        : $"{Value:F2} {Currency}";
}

/// <summary>
/// Price as integer ticks. HFT works in ticks, not decimals.
/// Integer math is faster, no floating-point errors, and order books are indexed by tick.
/// </summary>
public readonly record struct TickPrice(long Ticks, decimal TickSize) : IComparable<TickPrice>
{
    public decimal ToDecimal() => Ticks * TickSize;
    public Price ToPrice(Currency currency = default) => new(ToDecimal(), currency);

    public static TickPrice FromDecimal(decimal price, decimal tickSize) =>
        new((long)Math.Round(price / tickSize), tickSize);

    public static TickPrice FromPrice(Price price, decimal tickSize) =>
        FromDecimal(price.Value, tickSize);

    public int CompareTo(TickPrice other) => Ticks.CompareTo(other.Ticks);

    public static TickPrice operator +(TickPrice a, long ticks) => a with { Ticks = a.Ticks + ticks };
    public static TickPrice operator -(TickPrice a, long ticks) => a with { Ticks = a.Ticks - ticks };
    public static long operator -(TickPrice a, TickPrice b) => a.Ticks - b.Ticks;

    public static bool operator >(TickPrice a, TickPrice b) => a.Ticks > b.Ticks;
    public static bool operator <(TickPrice a, TickPrice b) => a.Ticks < b.Ticks;
    public static bool operator >=(TickPrice a, TickPrice b) => a.Ticks >= b.Ticks;
    public static bool operator <=(TickPrice a, TickPrice b) => a.Ticks <= b.Ticks;

    public override string ToString() => $"{Ticks}t ({ToDecimal():G})";
}

/// <summary>
/// A unit of account.
/// </summary>
public readonly record struct Currency(string Code)
{
    public static readonly Currency USD = new("USD");
    public static readonly Currency EUR = new("EUR");
    public static readonly Currency GBP = new("GBP");
    public static readonly Currency JPY = new("JPY");
    public static readonly Currency BTC = new("BTC");
    public static readonly Currency ETH = new("ETH");
    public static readonly Currency USDT = new("USDT");

    public static implicit operator Currency(string code) => new(code);
    public override string ToString() => Code;
}

/// <summary>
/// An amount of money.
/// </summary>
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money Zero(Currency c) => new(0m, c);
    public static Money USD(decimal amount) => new(amount, Currency.USD);

    public bool IsZero => Amount == 0m;
    public bool IsPositive => Amount > 0m;
    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, a.Currency);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, a.Currency);
    public static Money operator *(Money a, decimal b) => new(a.Amount * b, a.Currency);
    public static Money operator -(Money a) => new(-a.Amount, a.Currency);

    public override string ToString() => $"{Amount:N2} {Currency}";
}
