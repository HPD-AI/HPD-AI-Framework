using System.Globalization;
using System.Text;
using Rhodium.Events;

namespace Rhodium.Analytics;

public static class AccountTransferExporter
{
    public static string ToCsv(IEnumerable<AccountTransferStatusSnapshot> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);

        var sb = new StringBuilder();
        sb.AppendLine(
            "time,transfer_id,strategy_id,variant_id,destination_strategy_id,destination_variant_id,transfer_type,status,cash_amount,currency,instrument,asset_symbol,asset_class,venue,quantity,reason,external_reference");

        foreach (var transfer in transfers
            .OrderBy(static transfer => transfer.StatusAt)
            .ThenBy(static transfer => transfer.TransferId.Value))
        {
            var cashAmount = transfer.CashAmount?.Amount.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            var currency = transfer.CashAmount?.Currency.Code ?? string.Empty;
            var instrument = transfer.Instrument?.ToString() ?? string.Empty;
            var assetSymbol = transfer.Instrument?.Asset.Symbol ?? string.Empty;
            var assetClass = transfer.Instrument?.Asset.Class.ToString() ?? string.Empty;
            var venue = transfer.Instrument?.Venue.Name ?? string.Empty;
            var destinationStrategyId = transfer.DestinationStrategyId?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            var destinationVariantId = transfer.DestinationStrategyId.HasValue
                ? transfer.DestinationVariantId.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            sb.Append(EscapeCsv(transfer.StatusAt.ToString())).Append(',')
                .Append(transfer.TransferId.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(transfer.StrategyId.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(transfer.VariantId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(destinationStrategyId).Append(',')
                .Append(destinationVariantId).Append(',')
                .Append(transfer.TransferType).Append(',')
                .Append(transfer.Status).Append(',')
                .Append(cashAmount).Append(',')
                .Append(EscapeCsv(currency)).Append(',')
                .Append(EscapeCsv(instrument)).Append(',')
                .Append(EscapeCsv(assetSymbol)).Append(',')
                .Append(assetClass).Append(',')
                .Append(EscapeCsv(venue)).Append(',')
                .Append(transfer.Quantity.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(transfer.Reason ?? string.Empty)).Append(',')
                .Append(EscapeCsv(transfer.ExternalReference ?? string.Empty))
                .AppendLine();
        }

        return sb.ToString();
    }

    public static void ExportToCsv(IEnumerable<AccountTransferStatusSnapshot> transfers, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, ToCsv(transfers), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
}
