using Rhodium.Analytics;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

/// <summary>
/// Tests for BatchTearSheet and BatchTearSheetBuilder.
/// </summary>
public class BatchTearSheetTests
{
    [Fact]
    public void FromTearSheets_CreatesCorrectBatchStructure()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheets = new List<TearSheet>
        {
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital),
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(60000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital),
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital)
        };

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);

        Assert.Equal(3, batch.TotalReturn.Length);
        Assert.Equal(3, batch.Cagr.Length);
        Assert.Equal(3, batch.Sharpe.Length);
        Assert.Equal(3, batch.MaxDrawdown.Length);
    }

    [Fact]
    public void FromTearSheets_PreservesMetricValues()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var trades = new List<RoundTrip>
        {
            new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
        };

        var tearSheet = TearSheet.Calculate(trades, initialCapital);
        var batch = BatchTearSheetBuilder.FromTearSheets(new[] { tearSheet });

        Assert.Equal((double)tearSheet.TotalReturn, batch.TotalReturn.Span[0]);
        Assert.Equal((double)tearSheet.Cagr, batch.Cagr.Span[0]);
        Assert.Equal((double)tearSheet.SharpeRatio, batch.Sharpe.Span[0]);
        Assert.Equal((double)tearSheet.MaxDrawdown, batch.MaxDrawdown.Span[0]);
    }

    [Fact]
    public void FromTearSheets_HandlesEmptyList()
    {
        var batch = BatchTearSheetBuilder.FromTearSheets(Array.Empty<TearSheet>());

        Assert.Equal(0, batch.TotalReturn.Length);
        Assert.Equal(0, batch.Cagr.Length);
        Assert.Equal(0, batch.Sharpe.Length);
        Assert.Equal(0, batch.MaxDrawdown.Length);
    }

    [Fact]
    public void FromRoundTrips_GroupsByVariant()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var roundTripsByVariant = new Dictionary<int, List<RoundTrip>>
        {
            [0] = new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            },
            [1] = new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(60000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            },
            [2] = new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }
        };

        var batch = BatchTearSheetBuilder.FromRoundTrips(roundTripsByVariant, initialCapital, 3);

        Assert.Equal(3, batch.TotalReturn.Length);
        // Variant 1 should have highest return (100k profit vs 50k)
        Assert.True(batch.TotalReturn.Span[1] > batch.TotalReturn.Span[0]);
        // Variant 2 should have negative return
        Assert.True(batch.TotalReturn.Span[2] < 0);
    }

    [Fact]
    public void FromRoundTrips_HandlesGapsInVariants()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var roundTripsByVariant = new Dictionary<int, List<RoundTrip>>
        {
            [0] = new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            },
            // Variant 1 has no trades
            [2] = new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(60000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }
        };

        var batch = BatchTearSheetBuilder.FromRoundTrips(roundTripsByVariant, initialCapital, 3);

        // Variant 1 should have all zeros (no trades)
        Assert.Equal(0.0, batch.TotalReturn.Span[1]);
        Assert.Equal(0.0, batch.Cagr.Span[1]);
        Assert.Equal(0.0, batch.Sharpe.Span[1]);
        Assert.Equal(0.0, batch.MaxDrawdown.Span[1]);
    }

    [Fact]
    public void GetTopVariants_ReturnsBestByTotalReturn()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheets = new List<TearSheet>
        {
            // Variant 0: 0.499 return
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital),
            // Variant 1: 0.999 return (best)
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(60000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital),
            // Variant 2: -0.501 return (worst)
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital)
        };

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);
        var topVariants = BatchTearSheetBuilder.GetTopVariants(batch, BatchMetric.TotalReturn, 2);

        Assert.Equal(2, topVariants.Length);
        Assert.Equal(1, topVariants[0]); // Best variant
        Assert.Equal(0, topVariants[1]); // Second best
    }

    [Fact]
    public void GetTopVariants_HandlesSharpeRatio()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheets = new List<TearSheet>
        {
            // Multiple trades for Sharpe calculation
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD)),
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(51000m, Currency.USD), new Price(56000m, Currency.USD),
                    Instant.FromUnixMillis(3000), Instant.FromUnixMillis(4000), new Money(100m, Currency.USD))
            }, initialCapital),
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(52000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital)
        };

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);
        var topVariants = BatchTearSheetBuilder.GetTopVariants(batch, BatchMetric.Sharpe, 1);

        Assert.Single(topVariants);
        // First variant should have better Sharpe due to multiple positive trades
        Assert.Equal(0, topVariants[0]);
    }

    [Fact]
    public void GetSummary_CalculatesCorrectStatistics()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        var tearSheets = new List<TearSheet>
        {
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(55000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital),
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(60000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital),
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(55000m, Currency.USD), new Price(50000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital)
        };

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);
        var summary = BatchTearSheetBuilder.GetSummary(batch);

        // 2 positive, 1 negative
        Assert.Equal(2, summary.VariantsWithPositiveReturn);
        // Mean should be positive (2 wins, 1 loss)
        Assert.True(summary.MeanReturn > 0);
        // Best should be variant 1 (~0.999)
        Assert.InRange(summary.BestReturn, 0.99, 1.0);
        // Worst should be variant 2 (~-0.501)
        Assert.InRange(summary.WorstReturn, -0.51, -0.50);
    }

    [Fact]
    public void GetSummary_CalculatesMedianCorrectly()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        // Create 5 variants with known returns
        var tearSheets = new List<TearSheet>
        {
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(1m), new Price(100000m, Currency.USD), new Price(110000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD))
            }, initialCapital), // 10% return
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(1m), new Price(100000m, Currency.USD), new Price(120000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD))
            }, initialCapital), // 20% return
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(1m), new Price(100000m, Currency.USD), new Price(130000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD))
            }, initialCapital), // 30% return (median)
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(1m), new Price(100000m, Currency.USD), new Price(140000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD))
            }, initialCapital), // 40% return
            TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(1m), new Price(100000m, Currency.USD), new Price(150000m, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(0m, Currency.USD))
            }, initialCapital) // 50% return
        };

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);
        var summary = BatchTearSheetBuilder.GetSummary(batch);

        // Median should be 30% (0.30)
        Assert.InRange(summary.MedianReturn, 0.29, 0.31);
        // Mean should be 30% as well (linear distribution)
        Assert.InRange(summary.MeanReturn, 0.29, 0.31);
    }

    [Fact]
    public void BatchTearSheet_SupportsLargeVariantCounts()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var initialCapital = new Money(100000m, Currency.USD);

        // Create 1000 variants
        var tearSheets = new List<TearSheet>();
        for (int i = 0; i < 1000; i++)
        {
            var exitPrice = 50000m + (i * 10m); // Varying exit prices
            tearSheets.Add(TearSheet.Calculate(new List<RoundTrip>
            {
                new RoundTrip(inst, Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Price(exitPrice, Currency.USD),
                    Instant.FromUnixMillis(1000), Instant.FromUnixMillis(2000), new Money(100m, Currency.USD))
            }, initialCapital));
        }

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);

        Assert.Equal(1000, batch.TotalReturn.Length);
        Assert.Equal(1000, batch.Cagr.Length);
        Assert.Equal(1000, batch.Sharpe.Length);
        Assert.Equal(1000, batch.MaxDrawdown.Length);
    }
}
