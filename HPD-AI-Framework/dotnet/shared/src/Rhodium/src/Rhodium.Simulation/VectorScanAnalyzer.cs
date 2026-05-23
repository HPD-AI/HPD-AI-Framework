using System.Globalization;
using System.Text;
using Parquet;
using Parquet.Schema;

namespace Rhodium.Simulation;

public sealed class VectorScanAnalyzer
{
    private readonly SimulationResult _result;

    public VectorScanAnalyzer(SimulationResult result)
    {
        _result = result;
    }

    public IReadOnlyList<StrategyRunResult> TopBySharpe(int count)
        => TopBy(static run => (double)run.TearSheet.SharpeRatio, count, descending: true);

    public IReadOnlyList<StrategyRunResult> TopByTotalReturn(int count)
        => TopBy(static run => (double)run.TearSheet.TotalReturn, count, descending: true);

    public double[,] ToHeatmap(
        Func<StrategyRunResult, double> metric,
        string xParameter,
        string yParameter)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentException.ThrowIfNullOrWhiteSpace(xParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(yParameter);

        var xValues = DistinctParameterValues(xParameter);
        var yValues = DistinctParameterValues(yParameter);
        var heatmap = new double[yValues.Length, xValues.Length];
        var seen = new bool[yValues.Length, xValues.Length];

        foreach (var run in _result.Runs)
        {
            var x = Array.IndexOf(xValues, run.Parameters[xParameter]);
            var y = Array.IndexOf(yValues, run.Parameters[yParameter]);
            if (x < 0 || y < 0)
                continue;

            heatmap[y, x] = metric(run);
            seen[y, x] = true;
        }

        for (var y = 0; y < yValues.Length; y++)
        {
            for (var x = 0; x < xValues.Length; x++)
            {
                if (!seen[y, x])
                    heatmap[y, x] = double.NaN;
            }
        }

        return heatmap;
    }

    public IReadOnlyList<StrategyRunResult> Filter(
        double? minSharpe = null,
        decimal? maxDrawdown = null,
        decimal? minWinRate = null,
        int? minTrades = null)
    {
        IEnumerable<StrategyRunResult> runs = _result.Runs;

        if (minSharpe.HasValue)
            runs = runs.Where(run => (double)run.TearSheet.SharpeRatio >= minSharpe.Value);
        if (maxDrawdown.HasValue)
            runs = runs.Where(run => run.TearSheet.MaxDrawdown <= maxDrawdown.Value);
        if (minWinRate.HasValue)
            runs = runs.Where(run => run.TearSheet.WinRate >= minWinRate.Value);
        if (minTrades.HasValue)
            runs = runs.Where(run => run.TearSheet.TotalTrades >= minTrades.Value);

        return runs.ToArray();
    }

    public string ToCsv()
    {
        var parameterNames = _result.Runs
            .SelectMany(static run => run.Parameters.All.Select(static parameter => parameter.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        sb.Append("strategy_id,variant_index,total_return,sharpe,max_drawdown,win_rate,total_trades");
        foreach (var name in parameterNames)
            sb.Append(',').Append(EscapeCsv(name));
        sb.AppendLine();

        foreach (var run in _result.Runs.OrderBy(static run => run.VariantIndex))
        {
            sb.Append(run.StrategyId.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.VariantIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.TotalReturn.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.SharpeRatio.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.MaxDrawdown.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.WinRate.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.TotalTrades.ToString(CultureInfo.InvariantCulture));

            foreach (var name in parameterNames)
            {
                var value = run.Parameters.All.FirstOrDefault(parameter => parameter.Name == name).Value;
                sb.Append(',').Append(EscapeCsv(value?.ToString() ?? ""));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public void ExportToCsv(string path)
    {
        EnsureParentDirectory(path);
        File.WriteAllText(path, ToCsv(), Encoding.UTF8);
    }

    public void ExportToParquet(string path)
        => ExportToParquetAsync(path).GetAwaiter().GetResult();

    private async Task ExportToParquetAsync(string path, CancellationToken ct = default)
    {
        EnsureParentDirectory(path);

        var runs = _result.Runs.OrderBy(static run => run.VariantIndex).ToArray();
        var parameterNames = GetParameterNames();
        var fields = BuildParquetFields(parameterNames);
        var schema = new ParquetSchema(fields);

        await using var stream = File.Create(path);
        await using var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: ct);
        using var rowGroup = writer.CreateRowGroup();

        await rowGroup.WriteAsync<int>(fields[0], runs.Select(static run => run.StrategyId.Value).ToArray(), cancellationToken: ct);
        await rowGroup.WriteAsync<int>(fields[1], runs.Select(static run => run.VariantIndex).ToArray(), cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[2], runs.Select(static run => (double)run.TearSheet.TotalReturn).ToArray(), cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[3], runs.Select(static run => (double)run.TearSheet.SharpeRatio).ToArray(), cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[4], runs.Select(static run => (double)run.TearSheet.MaxDrawdown).ToArray(), cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[5], runs.Select(static run => (double)run.TearSheet.WinRate).ToArray(), cancellationToken: ct);
        await rowGroup.WriteAsync<int>(fields[6], runs.Select(static run => run.TearSheet.TotalTrades).ToArray(), cancellationToken: ct);

        for (var i = 0; i < parameterNames.Length; i++)
        {
            var name = parameterNames[i];
            var values = runs
                .Select(run => GetParameterValue(run, name)?.ToString() ?? "")
                .ToArray();
            ct.ThrowIfCancellationRequested();
            await rowGroup.WriteAsync(fields[7 + i], values);
        }
    }

    private IReadOnlyList<StrategyRunResult> TopBy(
        Func<StrategyRunResult, double> metric,
        int count,
        bool descending)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var ordered = descending
            ? _result.Runs.OrderByDescending(metric)
            : _result.Runs.OrderBy(metric);
        return ordered.Take(count).ToArray();
    }

    private object[] DistinctParameterValues(string parameter)
    {
        var values = new List<object>();
        foreach (var run in _result.Runs)
        {
            var value = run.Parameters[parameter];
            if (!values.Contains(value))
                values.Add(value);
        }

        return values.ToArray();
    }

    private string[] GetParameterNames()
        => _result.Runs
            .SelectMany(static run => run.Parameters.All.Select(static parameter => parameter.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static DataField[] BuildParquetFields(string[] parameterNames)
    {
        var fields = new DataField[7 + parameterNames.Length];
        fields[0] = new DataField<int>("strategy_id");
        fields[1] = new DataField<int>("variant_index");
        fields[2] = new DataField<double>("total_return");
        fields[3] = new DataField<double>("sharpe");
        fields[4] = new DataField<double>("max_drawdown");
        fields[5] = new DataField<double>("win_rate");
        fields[6] = new DataField<int>("total_trades");

        for (var i = 0; i < parameterNames.Length; i++)
            fields[7 + i] = new DataField<string>("param_" + SanitizeParquetFieldName(parameterNames[i]));

        return fields;
    }

    private static object? GetParameterValue(StrategyRunResult run, string name)
    {
        foreach (var parameter in run.Parameters.All)
        {
            if (parameter.Name == name)
                return parameter.Value;
        }

        return null;
    }

    private static string SanitizeParquetFieldName(string name)
    {
        if (name.Length == 0)
            return "value";

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        return builder.ToString();
    }

    private static void EnsureParentDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
}
