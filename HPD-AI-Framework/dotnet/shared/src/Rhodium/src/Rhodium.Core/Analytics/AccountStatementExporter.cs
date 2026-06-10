using System.Globalization;
using System.Text;
using Rhodium.Events;

namespace Rhodium.Analytics;

public static class AccountStatementExporter
{
    public static string ToCsv(IEnumerable<AccountStatementSnapshot> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var sb = new StringBuilder();
        sb.AppendLine(
            "time,strategy_id,variant_id,currency,cash,available_cash,pending_settlement,reserved_cash,market_value,equity,unrealized_pnl,realized_pnl,open_positions,open_orders");

        foreach (var statement in statements.OrderBy(static statement => statement.Time))
        {
            sb.Append(EscapeCsv(statement.Time.ToString())).Append(',')
                .Append(statement.StrategyId.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.VariantId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(statement.Currency.Code)).Append(',')
                .Append(statement.Cash.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.AvailableCash.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.PendingSettlement.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.ReservedCash.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.MarketValue.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.Equity.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.UnrealizedPnL.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.RealizedPnL.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.OpenPositions.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statement.OpenOrders.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return sb.ToString();
    }

    public static void ExportToCsv(IEnumerable<AccountStatementSnapshot> statements, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, ToCsv(statements), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
}
