namespace Rhodium.Primitives;

/// <summary>
/// Open-High-Low-Close-Volume bar.
/// </summary>
public readonly record struct Bar(
    Price Open,
    Price High,
    Price Low,
    Price Close,
    Qty Volume,
    Instant Time,
    Duration Period
)
{
    // Derived properties
    public Price Typical => new((High.Value + Low.Value + Close.Value) / 3m);
    public Price Median => new((High.Value + Low.Value) / 2m);
    public Price Range => High - Low;
    public Price Body => new(Math.Abs(Close.Value - Open.Value));
    public Price UpperWick => High - Price.Max(Open, Close);
    public Price LowerWick => Price.Min(Open, Close) - Low;

    public bool IsBullish => Close > Open;
    public bool IsBearish => Close < Open;
    public bool IsDoji => Body.Value < Range.Value * 0.1m;

    public decimal Change => Open.Value != 0 ? (Close.Value - Open.Value) / Open.Value : 0m;
    public decimal ChangeAbs => Close.Value - Open.Value;

    /// <summary>
    /// Update bar with a new price (for building bars from ticks).
    /// </summary>
    public Bar Update(Price price, Qty volume, Instant time) => this with
    {
        High = Price.Max(High, price),
        Low = Price.Min(Low, price),
        Close = price,
        Volume = Volume + volume,
        Time = time
    };

    public static Bar Create(Price price, Qty volume, Instant time, Duration period) =>
        new(price, price, price, price, volume, time, period);
}
