namespace Rhodium.Connectivity.Tests;

public class ExchangeIdTests
{
    [Fact]
    public void Constructor_SetsValue()
    {
        var exchangeId = new ExchangeId("TEST_EXCHANGE");

        Assert.Equal("TEST_EXCHANGE", exchangeId.Value);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var exchangeId = new ExchangeId("BINANCE");

        Assert.Equal("BINANCE", exchangeId.ToString());
    }

    [Fact]
    public void Replay_HasCorrectValue()
    {
        Assert.Equal("REPLAY", ExchangeId.Replay.Value);
    }

    [Fact]
    public void Binance_HasCorrectValue()
    {
        Assert.Equal("BINANCE", ExchangeId.Binance.Value);
    }

    [Fact]
    public void BinanceUS_HasCorrectValue()
    {
        Assert.Equal("BINANCE_US", ExchangeId.BinanceUS.Value);
    }

    [Fact]
    public void Coinbase_HasCorrectValue()
    {
        Assert.Equal("COINBASE", ExchangeId.Coinbase.Value);
    }

    [Fact]
    public void Kraken_HasCorrectValue()
    {
        Assert.Equal("KRAKEN", ExchangeId.Kraken.Value);
    }

    [Fact]
    public void Bybit_HasCorrectValue()
    {
        Assert.Equal("BYBIT", ExchangeId.Bybit.Value);
    }

    [Fact]
    public void Alpaca_HasCorrectValue()
    {
        Assert.Equal("ALPACA", ExchangeId.Alpaca.Value);
    }

    [Fact]
    public void InteractiveBrokers_HasCorrectValue()
    {
        Assert.Equal("IBKR", ExchangeId.InteractiveBrokers.Value);
    }

    [Fact]
    public void TDAmeritrade_HasCorrectValue()
    {
        Assert.Equal("TDA", ExchangeId.TDAmeritrade.Value);
    }

    [Fact]
    public void Equality_WorksCorrectly()
    {
        var id1 = new ExchangeId("BINANCE");
        var id2 = new ExchangeId("BINANCE");
        var id3 = new ExchangeId("COINBASE");

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
        Assert.True(id1 == id2);
        Assert.True(id1 != id3);
    }

    [Fact]
    public void StaticInstances_AreEqual()
    {
        var binance1 = ExchangeId.Binance;
        var binance2 = ExchangeId.Binance;

        Assert.Equal(binance1, binance2);
    }
}
