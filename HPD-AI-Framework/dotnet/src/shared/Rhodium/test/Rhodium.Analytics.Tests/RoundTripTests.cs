using Rhodium.Analytics;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

/// <summary>
/// Tests for RoundTrip struct.
/// </summary>
public class RoundTripTests
{
    [Fact]
    public void RoundTrip_CalculatesGrossPnLCorrectly_ForLongTrade()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(50m, Currency.USD)
        );

        // GrossPnL = (55000 - 50000) * 10 * 1 (long) = 50000
        Assert.Equal(50000m, rt.GrossPnL.Amount);
        Assert.Equal(Currency.USD, rt.GrossPnL.Currency);
    }

    [Fact]
    public void RoundTrip_CalculatesGrossPnLCorrectly_ForShortTrade()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Sell,
            Quantity: new Qty(10m),
            EntryPrice: new Price(55000m, Currency.USD),
            ExitPrice: new Price(50000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(50m, Currency.USD)
        );

        // GrossPnL = (50000 - 55000) * 10 * -1 (short) = 50000
        Assert.Equal(50000m, rt.GrossPnL.Amount);
    }

    [Fact]
    public void RoundTrip_CalculatesNetPnLCorrectly()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(100m, Currency.USD)
        );

        // NetPnL = 50000 - 100 = 49900
        Assert.Equal(49900m, rt.NetPnL.Amount);
    }

    [Fact]
    public void RoundTrip_CalculatesReturnPctCorrectly_ForLong()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(50m, Currency.USD)
        );

        // ReturnPct = (55000 - 50000) / 50000 * 1 = 0.1 = 10%
        Assert.Equal(0.1m, rt.ReturnPct);
    }

    [Fact]
    public void RoundTrip_CalculatesReturnPctCorrectly_ForShort()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Sell,
            Quantity: new Qty(10m),
            EntryPrice: new Price(55000m, Currency.USD),
            ExitPrice: new Price(50000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(50m, Currency.USD)
        );

        // ReturnPct = (50000 - 55000) / 55000 * -1 = 0.0909... ≈ 9.09%
        Assert.InRange(rt.ReturnPct, 0.09m, 0.10m);
    }

    [Fact]
    public void RoundTrip_CalculatesHoldingPeriodCorrectly()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(3000),
            Commission: new Money(50m, Currency.USD)
        );

        // HoldingPeriod = 3000 - 1000 = 2000ms = 2,000,000,000ns
        Assert.Equal(2_000_000_000L, rt.HoldingPeriod.Nanos);
    }

    [Fact]
    public void RoundTrip_IdentifiesWinCorrectly()
    {
        var win = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(100m, Currency.USD)
        );

        Assert.True(win.IsWin);
        Assert.False(win.IsLoss);
        Assert.False(win.IsBreakeven);
    }

    [Fact]
    public void RoundTrip_IdentifiesLossCorrectly()
    {
        var loss = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(55000m, Currency.USD),
            ExitPrice: new Price(50000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(100m, Currency.USD)
        );

        Assert.False(loss.IsWin);
        Assert.True(loss.IsLoss);
        Assert.False(loss.IsBreakeven);
    }

    [Fact]
    public void RoundTrip_IdentifiesBreakevenCorrectly()
    {
        var breakeven = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(50010m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(100m, Currency.USD)
        );

        // GrossPnL = 10 * 10 = 100, NetPnL = 100 - 100 = 0
        Assert.False(breakeven.IsWin);
        Assert.False(breakeven.IsLoss);
        Assert.True(breakeven.IsBreakeven);
    }

    [Fact]
    public void RoundTrip_CalculatesNotionalCorrectly()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(50000m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(50m, Currency.USD)
        );

        // Notional = 50000 * 10 = 500000
        Assert.Equal(500000m, rt.Notional.Amount);
        Assert.Equal(Currency.USD, rt.Notional.Currency);
    }

    [Fact]
    public void RoundTrip_HandlesZeroEntryPrice()
    {
        var rt = new RoundTrip(
            Instrument: new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            Side: Side.Buy,
            Quantity: new Qty(10m),
            EntryPrice: new Price(0m, Currency.USD),
            ExitPrice: new Price(55000m, Currency.USD),
            EntryTime: Instant.FromUnixMillis(1000),
            ExitTime: Instant.FromUnixMillis(2000),
            Commission: new Money(50m, Currency.USD)
        );

        // ReturnPct should be 0 to avoid division by zero
        Assert.Equal(0m, rt.ReturnPct);
    }
}
