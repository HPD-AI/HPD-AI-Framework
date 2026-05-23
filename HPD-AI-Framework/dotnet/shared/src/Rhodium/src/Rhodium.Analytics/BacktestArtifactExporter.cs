using System.Globalization;
using System.Text;
using Rhodium.Events;

namespace Rhodium.Analytics;

public static class BacktestArtifactExporter
{
    public const string ManifestFileName = "manifest.csv";
    public const string AccountStatementsFileName = "account_statements.csv";
    public const string CustodyPositionsFileName = "custody_positions.csv";
    public const string AccountTransfersFileName = "account_transfers.csv";

    public static BacktestArtifactManifest ExportToDirectory(
        IEnumerable<AccountStatementSnapshot> accountStatements,
        IEnumerable<CustodyPositionSnapshot> custodyPositions,
        string directory,
        IEnumerable<AccountTransferStatusSnapshot>? accountTransfers = null)
    {
        ArgumentNullException.ThrowIfNull(accountStatements);
        ArgumentNullException.ThrowIfNull(custodyPositions);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var accountStatementList = accountStatements.ToArray();
        var custodyPositionList = custodyPositions.ToArray();
        var accountTransferList = (accountTransfers ?? []).ToArray();
        Directory.CreateDirectory(directory);

        AccountStatementExporter.ExportToCsv(
            accountStatementList,
            Path.Combine(directory, AccountStatementsFileName));
        CustodyPositionExporter.ExportToCsv(
            custodyPositionList,
            Path.Combine(directory, CustodyPositionsFileName));
        AccountTransferExporter.ExportToCsv(
            accountTransferList,
            Path.Combine(directory, AccountTransfersFileName));

        var manifest = new BacktestArtifactManifest(
            AccountStatementsFileName,
            accountStatementList.Length,
            CustodyPositionsFileName,
            custodyPositionList.Length,
            AccountTransfersFileName,
            accountTransferList.Length,
            ManifestFileName);
        File.WriteAllText(Path.Combine(directory, ManifestFileName), ToManifestCsv(manifest), Encoding.UTF8);
        return manifest;
    }

    public static string ToManifestCsv(BacktestArtifactManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("artifact,file_name,row_count");
        AppendManifestRow(sb, "account_statements", manifest.AccountStatementsFileName, manifest.AccountStatementCount);
        AppendManifestRow(sb, "custody_positions", manifest.CustodyPositionsFileName, manifest.CustodyPositionCount);
        AppendManifestRow(sb, "account_transfers", manifest.AccountTransfersFileName, manifest.AccountTransferCount);
        return sb.ToString();
    }

    private static void AppendManifestRow(StringBuilder sb, string artifact, string fileName, int rowCount)
    {
        sb.Append(artifact).Append(',')
            .Append(EscapeCsv(fileName)).Append(',')
            .Append(rowCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine();
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
}

public readonly record struct BacktestArtifactManifest(
    string AccountStatementsFileName,
    int AccountStatementCount,
    string CustodyPositionsFileName,
    int CustodyPositionCount,
    string AccountTransfersFileName,
    int AccountTransferCount,
    string ManifestFileName);
