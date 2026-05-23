using Rhodium.Analytics;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

public sealed class AccountStatementExporterTests
{
    [Fact]
    public void ToCsv_ExportsStatementFieldsInTimeOrder()
    {
        var firstTime = Instant.FromUnixSeconds(10);
        var secondTime = Instant.FromUnixSeconds(20);
        var statements = new[]
        {
            CreateStatement(new StrategyId(2), 1, secondTime, Money.USD(200m), Money.USD(190m), Money.USD(10m)),
            CreateStatement(new StrategyId(1), 0, firstTime, Money.USD(100m), Money.USD(75m), Money.USD(25m))
        };

        var csv = AccountStatementExporter.ToCsv(statements);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "time,strategy_id,variant_id,currency,cash,available_cash,pending_settlement,reserved_cash,market_value,equity,unrealized_pnl,realized_pnl,open_positions,open_orders",
            lines[0]);
        Assert.StartsWith(firstTime.ToString() + ",1,0,USD,100,75,25,5,50,125,2,-1,3,4", lines[1]);
        Assert.StartsWith(secondTime.ToString() + ",2,1,USD,200,190,10,5,50,210,2,-1,3,4", lines[2]);
    }

    [Fact]
    public void ExportToCsv_CreatesParentDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rhodium-account-statements-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nested", "statements.csv");

        try
        {
            AccountStatementExporter.ExportToCsv(
                [CreateStatement(new StrategyId(7), 3, Instant.FromUnixSeconds(1), Money.USD(1m), Money.USD(1m), Money.USD(0m))],
                path);

            var csv = File.ReadAllText(path);
            Assert.Contains(",7,3,USD,1,1,0,", csv);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static AccountStatementSnapshot CreateStatement(
        StrategyId strategyId,
        int variantId,
        Instant time,
        Money cash,
        Money availableCash,
        Money pendingSettlement)
        => new(
            strategyId,
            variantId,
            cash.Currency,
            cash,
            availableCash,
            pendingSettlement,
            ReservedCash: Money.USD(5m),
            MarketValue: Money.USD(50m),
            Equity: cash + pendingSettlement,
            UnrealizedPnL: Money.USD(2m),
            RealizedPnL: Money.USD(-1m),
            OpenPositions: 3,
            OpenOrders: 4)
        {
            Time = time
        };
}
