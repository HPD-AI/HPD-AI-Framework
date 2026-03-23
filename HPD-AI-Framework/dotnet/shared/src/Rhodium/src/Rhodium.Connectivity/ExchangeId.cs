namespace Rhodium.Connectivity;

/// <summary>
/// Exchange identifier.
/// </summary>
public readonly record struct ExchangeId(string Value)
{
    // Simulation
    public static readonly ExchangeId Replay = new("REPLAY");

    // Crypto
    public static readonly ExchangeId Binance = new("BINANCE");
    public static readonly ExchangeId BinanceUS = new("BINANCE_US");
    public static readonly ExchangeId Coinbase = new("COINBASE");
    public static readonly ExchangeId Kraken = new("KRAKEN");
    public static readonly ExchangeId Bybit = new("BYBIT");

    // Equities
    public static readonly ExchangeId Alpaca = new("ALPACA");
    public static readonly ExchangeId InteractiveBrokers = new("IBKR");
    public static readonly ExchangeId TDAmeritrade = new("TDA");

    public override string ToString() => Value;
}
