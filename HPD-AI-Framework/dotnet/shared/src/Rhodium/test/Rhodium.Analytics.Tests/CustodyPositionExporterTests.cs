using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

public sealed class CustodyPositionExporterTests
{
    [Fact]
    public void ToCsv_ExportsCustodyFieldsInStableTimeOrder()
    {
        var firstTime = Instant.FromUnixSeconds(10);
        var secondTime = Instant.FromUnixSeconds(20);
        var positions = new[]
        {
            CreateSnapshot(new StrategyId(2), 1, secondTime, new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NASDAQ), new Qty(2m), new Qty(2m), Qty.Zero),
            CreateSnapshot(new StrategyId(1), 0, firstTime, new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NYSE), new Qty(3m), new Qty(1m), new Qty(2m))
        };

        var csv = CustodyPositionExporter.ToCsv(positions);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "time,strategy_id,variant_id,instrument,asset_symbol,asset_class,venue,quantity,settled_quantity,pending_delivery_quantity,rehypothecatable_quantity,avg_entry_price,mark_price,currency,market_value,unrealized_pnl,realized_pnl,is_open",
            lines[0]);
        Assert.StartsWith(firstTime.ToString() + ",1,0,AAPL@NYSE,AAPL,Equity,NYSE,3,1,2,1,100,101,USD,303,3,-1,true", lines[1]);
        Assert.StartsWith(secondTime.ToString() + ",2,1,MSFT@NASDAQ,MSFT,Equity,NASDAQ,2,2,0,2,100,101,USD,202,2,-1,true", lines[2]);
    }

    [Fact]
    public void ExportToCsv_CreatesParentDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rhodium-custody-positions-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nested", "custody.csv");

        try
        {
            CustodyPositionExporter.ExportToCsv(
                [CreateSnapshot(new StrategyId(7), 3, Instant.FromUnixSeconds(1), new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ), new Qty(1m), new Qty(1m), Qty.Zero)],
                path);

            var csv = File.ReadAllText(path);
            Assert.Contains(",7,3,AAPL@NASDAQ,AAPL,Equity,NASDAQ,1,1,0,1,", csv);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static CustodyPositionSnapshot CreateSnapshot(
        StrategyId strategyId,
        int variantId,
        Instant time,
        Instrument instrument,
        Qty quantity,
        Qty settledQuantity,
        Qty pendingDeliveryQuantity)
        => new(
            strategyId,
            variantId,
            instrument,
            quantity,
            settledQuantity,
            pendingDeliveryQuantity,
            RehypothecatableQuantity: settledQuantity.Value > 0m ? settledQuantity : Qty.Zero,
            AvgEntryPrice: new Price(100m, Currency.USD),
            MarkPrice: new Price(101m, Currency.USD),
            MarketValue: new Money(quantity.Value * 101m, Currency.USD),
            UnrealizedPnL: new Money(quantity.Value, Currency.USD),
            RealizedPnL: Money.USD(-1m),
            IsOpen: !quantity.IsZero)
        {
            Time = time
        };
}
