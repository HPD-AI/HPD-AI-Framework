using Rhodium.Analytics;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

/// <summary>
/// Tests for TearSheet performance metrics calculation.
/// </summary>
public class TearSheetTests
{
    [Fact]
    public void Calculate_ReturnsEmpty_ForNoTrades()
    {
        var trades = new List<RoundTrip>();
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        Assert.Equal(0m, tearSheet.TotalReturn);
        Assert.Equal(0m, tearSheet.Cagr);
        Assert.Equal(0, tearSheet.TotalTrades);
        Assert.Equal(0m, tearSheet.TotalPnL.Amount);
    }

    [Fact]
    public void Calculate_CalculatesTotalReturn_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            new RoundTrip(
                Instrument: inst,
                Side: Side.Buy,
                Quantity: new Qty(10m),
                EntryPrice: new Price(50000m, Currency.USD),
                ExitPrice: new Price(55000m, Currency.USD),
                EntryTime: Instant.FromUnixMillis(1000),
                ExitTime: Instant.FromUnixMillis(2000),
                Commission: new Money(100m, Currency.USD)
            )
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // GrossPnL = 50000, Commission = 100, NetPnL = 49900
        // TotalReturn = 49900 / 100000 = 0.499 = 49.9%
        Assert.Equal(0.499m, tearSheet.TotalReturn);
    }

    [Fact]
    public void Calculate_CalculatesWinRate_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Win
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Win
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(51000m, Currency.USD), new Price(56000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(100m, Currency.USD)),
            // Loss
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                Instant.FromUnixMillis(5000), Instant.FromUnixMillis(6000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // 2 wins / 3 total = 0.6666... ≈ 66.67%
        Assert.InRange(tearSheet.WinRate, 0.66m, 0.67m);
        Assert.Equal(2, tearSheet.WinningTrades);
        Assert.Equal(1, tearSheet.LosingTrades);
        Assert.Equal(3, tearSheet.TotalTrades);
    }

    [Fact]
    public void Calculate_CalculatesProfitFactor_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Win +49900
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Loss -50100
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // ProfitFactor = 49900 / 50100 ≈ 0.996
        Assert.InRange(tearSheet.ProfitFactor, 0.99m, 1.0m);
    }

    [Fact]
    public void Calculate_CalculatesPayoffRatio_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Win +49900
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Win +49900
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Loss -50100
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // AvgWin = 49900, AvgLoss = -50100
        // PayoffRatio = 49900 / 50100 ≈ 0.996
        Assert.InRange(tearSheet.PayoffRatio, 0.99m, 1.0m);
    }

    [Fact]
    public void Calculate_CalculatesExpectancy_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Win +49900
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Loss -50100
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // Expectancy = (49900 - 50100) / 2 = -100
        Assert.Equal(-100m, tearSheet.ExpectancyPerTrade);
    }

    [Fact]
    public void Calculate_CalculatesMaxDrawdown_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);
        var trades = new List<RoundTrip>
        {
            // Win +10000 (equity = 110000, peak = 110000)
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(51000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD)),
            // Loss -20000 (equity = 90000, DD = 20000/110000 = 18.18%)
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(53000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(0m, Currency.USD)),
            // Win +10000 (equity = 100000)
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(51000m, Currency.USD),
                Instant.FromUnixMillis(5000), Instant.FromUnixMillis(6000), new Money(0m, Currency.USD))
        };

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // Max DD = (110000 - 90000) / 110000 ≈ 0.1818 = 18.18%
        Assert.InRange(tearSheet.MaxDrawdown, 0.18m, 0.19m);
    }

    [Fact]
    public void Calculate_CalculatesAvgHoldingPeriod_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Holding period = 1000ms
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(0), Instant.FromUnixMillis(1000), new Money(0m, Currency.USD)),
            // Holding period = 3000ms
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(0), Instant.FromUnixMillis(3000), new Money(0m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // Avg = (1000 + 3000) / 2 = 2000ms = 2,000,000,000ns
        Assert.Equal(2_000_000_000L, tearSheet.AvgHoldingPeriod.Nanos);
    }

    [Fact]
    public void Calculate_CalculatesGrossPnL_AndCommissions()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        Assert.Equal(50000m, tearSheet.GrossPnL.Amount);
        Assert.Equal(100m, tearSheet.TotalCommissions.Amount);
        Assert.Equal(49900m, tearSheet.TotalPnL.Amount);
    }

    [Fact]
    public void Calculate_CalculatesLargestWinAndLoss()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Win +49900
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Win +99900 (largest win)
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(60000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
            // Loss -50100 (largest loss)
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        Assert.Equal(99900m, tearSheet.LargestWin.Amount);
        Assert.Equal(-50100m, tearSheet.LargestLoss.Amount);
    }

    [Fact]
    public void Calculate_CalculatesPeriod_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD)),
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(5000), Instant.FromUnixMillis(10000), new Money(0m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // Period = min entry (1000) to max exit (10000)
        Assert.Equal(1000L * 1_000_000, tearSheet.Period.Start.Nanos);
        Assert.Equal(10000L * 1_000_000, tearSheet.Period.End.Nanos);
    }

    [Fact]
    public void Calculate_HandlesSharpeRatio_WhenZeroStdDev()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            // Same return for all trades
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD)),
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(0m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // When all returns are identical, stdDev is 0, so Sharpe should be 0
        Assert.Equal(0m, tearSheet.SharpeRatio);
    }

    [Fact]
    public void Calculate_CalculatesCAGR_Correctly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        // Create trades spanning 1 year with 10% return
        var startTime = Instant.FromUnixMillis(0);
        var endTime = Instant.FromUnixMillis((long)(365.25 * 24 * 60 * 60 * 1000)); // 1 year
        var trades = new List<RoundTrip>
        {
            new RoundTrip(inst, Side.Buy, new Qty(1m), new Price(100000m, Currency.USD), new Price(110000m, Currency.USD),
                startTime, endTime, new Money(0m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);

        // TotalReturn = 10%, CAGR over 1 year = 10%
        Assert.InRange(tearSheet.Cagr, 0.09m, 0.11m);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trades = new List<RoundTrip>
        {
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
        };
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheet = TearSheet.Calculate(trades, initialCapital);
        var output = tearSheet.ToString();

        Assert.Contains("Performance Summary", output);
        Assert.Contains("Total Return", output);
        Assert.Contains("CAGR", output);
        Assert.Contains("Sharpe Ratio", output);
        Assert.Contains("Win Rate", output);
        Assert.Contains("Total: 1", output);
    }
}
