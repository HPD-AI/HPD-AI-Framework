using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

public sealed class AccountTransferExporterTests
{
    [Fact]
    public void ToCsv_ExportsTransferRows()
    {
        var transfers = new[]
        {
            new AccountTransferStatusSnapshot(
                new AccountTransferId(1),
                new StrategyId(7),
                VariantId: 0,
                AccountTransferType.CashDeposit,
                AccountTransferStatus.Completed,
                CashAmount: Money.USD(100m),
                Instrument: null,
                Quantity: Qty.Zero,
                StatusAt: Instant.FromUnixSeconds(2),
                Reason: null,
                ExternalReference: "ach-1",
                DestinationStrategyId: new StrategyId(8),
                DestinationVariantId: 1),
            new AccountTransferStatusSnapshot(
                new AccountTransferId(2),
                new StrategyId(7),
                VariantId: 0,
                AccountTransferType.AssetWithdrawal,
                AccountTransferStatus.Failed,
                CashAmount: null,
                new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ),
                Quantity: new Qty(3m),
                StatusAt: Instant.FromUnixSeconds(1),
                Reason: "insufficient custody",
                ExternalReference: "xfer-2")
        };

        var csv = AccountTransferExporter.ToCsv(transfers);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "time,transfer_id,strategy_id,variant_id,destination_strategy_id,destination_variant_id,transfer_type,status,cash_amount,currency,instrument,asset_symbol,asset_class,venue,quantity,reason,external_reference",
            lines[0]);
        Assert.Contains(",2,7,0,,,AssetWithdrawal,Failed,,,AAPL@NASDAQ,AAPL,Equity,NASDAQ,3,insufficient custody,xfer-2", lines[1]);
        Assert.Contains(",1,7,0,8,1,CashDeposit,Completed,100,USD,,,,,0,,ach-1", lines[2]);
    }

    [Fact]
    public void ExportToCsv_WritesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rhodium-transfers-{Guid.NewGuid():N}.csv");

        try
        {
            AccountTransferExporter.ExportToCsv(
                [
                    new AccountTransferStatusSnapshot(
                        new AccountTransferId(1),
                        new StrategyId(7),
                        VariantId: 0,
                        AccountTransferType.CashWithdrawal,
                        AccountTransferStatus.Canceled,
                        CashAmount: Money.USD(25m),
                        Instrument: null,
                        Quantity: Qty.Zero,
                        StatusAt: Instant.FromUnixSeconds(1),
                        Reason: "user canceled",
                        ExternalReference: "ach-2")
                ],
                path);

            Assert.True(File.Exists(path));
            Assert.Contains("CashWithdrawal,Canceled,25,USD", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
