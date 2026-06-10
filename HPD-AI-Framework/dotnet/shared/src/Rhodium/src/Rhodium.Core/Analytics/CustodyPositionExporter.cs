using System.Globalization;
using System.Text;
using Rhodium.Events;

namespace Rhodium.Analytics;

public static class CustodyPositionExporter
{
    public static string ToCsv(IEnumerable<CustodyPositionSnapshot> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var sb = new StringBuilder();
        sb.AppendLine(
            "time,strategy_id,variant_id,instrument,asset_symbol,asset_class,venue,quantity,settled_quantity,pending_delivery_quantity,rehypothecatable_quantity,avg_entry_price,mark_price,currency,market_value,unrealized_pnl,realized_pnl,is_open");

        foreach (var position in positions
            .OrderBy(static position => position.Time)
            .ThenBy(static position => position.StrategyId.Value)
            .ThenBy(static position => position.VariantId)
            .ThenBy(static position => position.Instrument.Asset.Symbol, StringComparer.Ordinal)
            .ThenBy(static position => position.Instrument.Venue.Name, StringComparer.Ordinal))
        {
            var currency = position.MarkPrice.Currency == default
                ? position.AvgEntryPrice.Currency
                : position.MarkPrice.Currency;

            sb.Append(EscapeCsv(position.Time.ToString())).Append(',')
                .Append(position.StrategyId.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.VariantId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(position.Instrument.ToString())).Append(',')
                .Append(EscapeCsv(position.Instrument.Asset.Symbol)).Append(',')
                .Append(position.Instrument.Asset.Class).Append(',')
                .Append(EscapeCsv(position.Instrument.Venue.Name)).Append(',')
                .Append(position.Quantity.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.SettledQuantity.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.PendingDeliveryQuantity.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.RehypothecatableQuantity.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.AvgEntryPrice.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.MarkPrice.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(currency.Code)).Append(',')
                .Append(position.MarketValue.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.UnrealizedPnL.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.RealizedPnL.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(position.IsOpen ? "true" : "false")
                .AppendLine();
        }

        return sb.ToString();
    }

    public static void ExportToCsv(IEnumerable<CustodyPositionSnapshot> positions, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, ToCsv(positions), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
}
