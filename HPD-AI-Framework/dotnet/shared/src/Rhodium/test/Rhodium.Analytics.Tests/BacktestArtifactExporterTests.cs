using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

public sealed class BacktestArtifactExporterTests
{
    [Fact]
    public void ExportToDirectory_WritesAccountCustodyAndManifestFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rhodium-artifacts-{Guid.NewGuid():N}");

        try
        {
            var manifest = BacktestArtifactExporter.ExportToDirectory(
                [
                    CreateStatement(new StrategyId(7), 0, Instant.FromUnixSeconds(1)),
                    CreateStatement(new StrategyId(7), 0, Instant.FromUnixSeconds(2))
                ],
                [CreateCustody(new StrategyId(7), 0, Instant.FromUnixSeconds(1))],
                directory,
                [CreateTransfer(new StrategyId(7), 0, Instant.FromUnixSeconds(1))]);

            Assert.Equal(BacktestArtifactExporter.AccountStatementsFileName, manifest.AccountStatementsFileName);
            Assert.Equal(2, manifest.AccountStatementCount);
            Assert.Equal(BacktestArtifactExporter.CustodyPositionsFileName, manifest.CustodyPositionsFileName);
            Assert.Equal(1, manifest.CustodyPositionCount);
            Assert.Equal(BacktestArtifactExporter.AccountTransfersFileName, manifest.AccountTransfersFileName);
            Assert.Equal(1, manifest.AccountTransferCount);
            Assert.Equal(BacktestArtifactExporter.ManifestFileName, manifest.ManifestFileName);

            var accountPath = Path.Combine(directory, BacktestArtifactExporter.AccountStatementsFileName);
            var custodyPath = Path.Combine(directory, BacktestArtifactExporter.CustodyPositionsFileName);
            var transfersPath = Path.Combine(directory, BacktestArtifactExporter.AccountTransfersFileName);
            var manifestPath = Path.Combine(directory, BacktestArtifactExporter.ManifestFileName);

            Assert.True(File.Exists(accountPath));
            Assert.True(File.Exists(custodyPath));
            Assert.True(File.Exists(transfersPath));
            Assert.True(File.Exists(manifestPath));
            Assert.Contains("account_statements,account_statements.csv,2", File.ReadAllText(manifestPath));
            Assert.Contains("custody_positions,custody_positions.csv,1", File.ReadAllText(manifestPath));
            Assert.Contains("account_transfers,account_transfers.csv,1", File.ReadAllText(manifestPath));
            Assert.Contains(",7,0,USD,", File.ReadAllText(accountPath));
            Assert.Contains(",7,0,AAPL@NASDAQ,AAPL,Equity,NASDAQ,", File.ReadAllText(custodyPath));
            Assert.Contains(",7,0,,,CashDeposit,Completed,100,USD", File.ReadAllText(transfersPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ToManifestCsv_ExportsManifestRows()
    {
        var manifest = new BacktestArtifactManifest(
            AccountStatementsFileName: "accounts.csv",
            AccountStatementCount: 2,
            CustodyPositionsFileName: "custody.csv",
            CustodyPositionCount: 3,
            AccountTransfersFileName: "transfers.csv",
            AccountTransferCount: 4,
            ManifestFileName: "manifest.csv");

        var csv = BacktestArtifactExporter.ToManifestCsv(manifest);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("artifact,file_name,row_count", lines[0]);
        Assert.Equal("account_statements,accounts.csv,2", lines[1]);
        Assert.Equal("custody_positions,custody.csv,3", lines[2]);
        Assert.Equal("account_transfers,transfers.csv,4", lines[3]);
    }

    private static AccountStatementSnapshot CreateStatement(
        StrategyId strategyId,
        int variantId,
        Instant time)
        => new(
            strategyId,
            variantId,
            Currency.USD,
            Cash: Money.USD(100m),
            AvailableCash: Money.USD(90m),
            PendingSettlement: Money.USD(10m),
            ReservedCash: Money.USD(0m),
            MarketValue: Money.USD(50m),
            Equity: Money.USD(160m),
            UnrealizedPnL: Money.USD(1m),
            RealizedPnL: Money.USD(-1m),
            OpenPositions: 1,
            OpenOrders: 0)
        {
            Time = time
        };

    private static CustodyPositionSnapshot CreateCustody(
        StrategyId strategyId,
        int variantId,
        Instant time)
        => new(
            strategyId,
            variantId,
            new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ),
            Quantity: new Qty(1m),
            SettledQuantity: new Qty(1m),
            PendingDeliveryQuantity: Qty.Zero,
            RehypothecatableQuantity: new Qty(1m),
            AvgEntryPrice: new Price(100m, Currency.USD),
            MarkPrice: new Price(101m, Currency.USD),
            MarketValue: Money.USD(101m),
            UnrealizedPnL: Money.USD(1m),
            RealizedPnL: Money.USD(0m),
            IsOpen: true)
        {
            Time = time
        };

    private static AccountTransferStatusSnapshot CreateTransfer(
        StrategyId strategyId,
        int variantId,
        Instant time)
        => new(
            new AccountTransferId(1),
            strategyId,
            variantId,
            AccountTransferType.CashDeposit,
            AccountTransferStatus.Completed,
            CashAmount: Money.USD(100m),
            Instrument: null,
            Quantity: Qty.Zero,
            StatusAt: time,
            Reason: null,
            ExternalReference: "ach-1")
        {
            Time = time
        };
}
