namespace Rhodium.Primitives;

/// <summary>
/// A tradeable thing. Just identity, nothing else.
/// </summary>
public readonly record struct Asset(
    string Symbol,
    AssetClass Class,
    string? Underlying = null  // For derivatives
) : IComparable<Asset>
{
    public int CompareTo(Asset other) => string.CompareOrdinal(Symbol, other.Symbol);
    public override string ToString() => Symbol;

    public static implicit operator Asset(string symbol) => new(symbol, AssetClass.Equity);
}

/// <summary>
/// Where trading happens.
/// </summary>
public readonly record struct Venue(string Name)
{
    public static readonly Venue NYSE = new("NYSE");
    public static readonly Venue NASDAQ = new("NASDAQ");
    public static readonly Venue CME = new("CME");
    public static readonly Venue Binance = new("Binance");
    public static readonly Venue Coinbase = new("Coinbase");
    public static readonly Venue Unknown = new("UNKNOWN");

    public static implicit operator Venue(string name) => new(name);
    public override string ToString() => Name;
}

/// <summary>
/// Asset + Venue = where to trade what.
/// </summary>
public readonly record struct Instrument(Asset Asset, Venue Venue)
{
    public static readonly Instrument Unknown = new(new Asset("UNKNOWN", AssetClass.Equity), Venue.Unknown);
    public override string ToString() => $"{Asset}@{Venue}";
}

/// <summary>
/// Classification of asset.
/// </summary>
public enum AssetClass : byte
{
    Equity = 1,
    Option = 2,
    Future = 3,
    Forex = 4,
    Crypto = 5,
    Bond = 6,
    Index = 7,
    Commodity = 8
}
