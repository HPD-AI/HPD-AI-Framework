namespace Rhodium.Primitives;

/// <summary>
/// Static metadata about an instrument. What you need to trade it correctly.
/// This is instrument-intrinsic data, not configuration.
/// </summary>
public readonly record struct SecurityMetadata(
    Instrument Instrument,
    decimal TickSize,
    decimal LotSize,
    Currency Currency = default,
    decimal Multiplier = 1m,
    ContractType ContractType = ContractType.Spot,

    // Derivatives (null if not applicable)
    Instant? Expiry = null,
    Price? Strike = null,
    OptionType? OptionType = null,
    Instrument? Underlying = null,

    // Trading constraints
    Qty? MinQty = null,
    Qty? MaxQty = null,
    Money? MinNotional = null
)
{
    public TickPrice ToTick(Price price) => TickPrice.FromPrice(price, TickSize);
    public Price FromTick(TickPrice tick) => tick.ToPrice(Currency);

    public Qty RoundToLot(Qty qty) => new(Math.Floor(qty.Value / LotSize) * LotSize);

    public bool IsDerivative => Expiry.HasValue || Underlying.HasValue;
    public bool IsOption => OptionType.HasValue;

    public static SecurityMetadata Default(Instrument inst) =>
        new(inst, TickSize: 0.01m, LotSize: 1m);

    public static SecurityMetadata Equity(Instrument inst, decimal tickSize = 0.01m) =>
        new(inst, tickSize, LotSize: 1m, ContractType: ContractType.Spot);

    public static SecurityMetadata Crypto(Instrument inst, decimal tickSize, decimal lotSize) =>
        new(inst, tickSize, lotSize, ContractType: ContractType.Spot);

    public static SecurityMetadata Future(
        Instrument inst,
        decimal tickSize,
        decimal multiplier,
        Instant expiry,
        Instrument underlying) =>
        new(inst, tickSize, LotSize: 1m, Multiplier: multiplier,
            ContractType: ContractType.LinearPerp, Expiry: expiry, Underlying: underlying);
}

public enum OptionType : byte { Call, Put }

public enum ContractType : byte
{
    Spot,
    LinearPerp,
    InversePerp,
    Future,
    Option
}
