using System.Globalization;
using System.Text;
using Parquet;
using Parquet.Schema;

namespace Rhodium.Simulation;

/// <summary>
/// Analyzer for strategy-grid simulation results.
/// </summary>
public sealed class VectorScanAnalyzer
{
    private readonly SimulationResult _result;

    /// <summary>Create an analyzer for a completed simulation result.</summary>
    public VectorScanAnalyzer(SimulationResult result)
    {
        _result = result;
    }

    /// <summary>Return the top runs by Sharpe ratio.</summary>
    public IReadOnlyList<StrategyRunResult> TopBySharpe(int count)
        => TopBy(static run => (double)run.TearSheet.SharpeRatio, count, descending: true);

    /// <summary>Return the top runs by total return.</summary>
    public IReadOnlyList<StrategyRunResult> TopByTotalReturn(int count)
        => TopBy(static run => (double)run.TearSheet.TotalReturn, count, descending: true);

    /// <summary>Create a two-dimensional parameter heatmap for a metric.</summary>
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

    /// <summary>Filter runs by common performance thresholds.</summary>
    public IReadOnlyList<StrategyRunResult> Filter(
        double? minSharpe = null,
        decimal? maxDrawdown = null,
        decimal? minWinRate = null,
        int? minTrades = null)
    {
        var filtered = new List<StrategyRunResult>(_result.Runs.Count);
        for (var i = 0; i < _result.Runs.Count; i++)
        {
            var run = _result.Runs[i];
            if (minSharpe.HasValue && (double)run.TearSheet.SharpeRatio < minSharpe.Value)
                continue;
            if (maxDrawdown.HasValue && run.TearSheet.MaxDrawdown > maxDrawdown.Value)
                continue;
            if (minWinRate.HasValue && run.TearSheet.WinRate < minWinRate.Value)
                continue;
            if (minTrades.HasValue && run.TearSheet.TotalTrades < minTrades.Value)
                continue;

            filtered.Add(run);
        }

        return filtered.ToArray();
    }

    /// <summary>Export the analyzed result as CSV text.</summary>
    public string ToCsv()
    {
        var parameterNames = GetParameterNames();

        var sb = new StringBuilder();
        sb.Append("strategy_id,variant_index,total_return,sharpe,max_drawdown,win_rate,total_trades");
        foreach (var name in parameterNames)
            sb.Append(',').Append(EscapeCsv(name));
        sb.AppendLine();

        var runs = CopyRunsByVariantIndex();
        for (var i = 0; i < runs.Length; i++)
        {
            var run = runs[i];
            sb.Append(run.StrategyId.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.VariantIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.TotalReturn.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.SharpeRatio.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.MaxDrawdown.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.WinRate.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TearSheet.TotalTrades.ToString(CultureInfo.InvariantCulture));

            foreach (var name in parameterNames)
            {
                _ = run.Parameters.TryGet(name, out var value);
                sb.Append(',').Append(EscapeCsv(value?.ToString() ?? ""));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Write the analyzed result to a CSV file.</summary>
    public void ExportToCsv(string path)
    {
        EnsureParentDirectory(path);
        File.WriteAllText(path, ToCsv(), Encoding.UTF8);
    }

    /// <summary>Write the analyzed result to a Parquet file.</summary>
    public void ExportToParquet(string path)
        => ExportToParquetAsync(path).GetAwaiter().GetResult();

    private async Task ExportToParquetAsync(string path, CancellationToken ct = default)
    {
        EnsureParentDirectory(path);

        var runs = CopyRunsByVariantIndex();
        var parameterNames = GetParameterNames();
        var fields = BuildParquetFields(parameterNames);
        var schema = new ParquetSchema(fields);

        await using var stream = File.Create(path);
        await using var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: ct);
        using var rowGroup = writer.CreateRowGroup();

        var strategyIds = new int[runs.Length];
        var variantIndexes = new int[runs.Length];
        var totalReturns = new double[runs.Length];
        var sharpes = new double[runs.Length];
        var maxDrawdowns = new double[runs.Length];
        var winRates = new double[runs.Length];
        var totalTrades = new int[runs.Length];
        for (var i = 0; i < runs.Length; i++)
        {
            var run = runs[i];
            strategyIds[i] = run.StrategyId.Value;
            variantIndexes[i] = run.VariantIndex;
            totalReturns[i] = (double)run.TearSheet.TotalReturn;
            sharpes[i] = (double)run.TearSheet.SharpeRatio;
            maxDrawdowns[i] = (double)run.TearSheet.MaxDrawdown;
            winRates[i] = (double)run.TearSheet.WinRate;
            totalTrades[i] = run.TearSheet.TotalTrades;
        }

        await rowGroup.WriteAsync<int>(fields[0], strategyIds, cancellationToken: ct);
        await rowGroup.WriteAsync<int>(fields[1], variantIndexes, cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[2], totalReturns, cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[3], sharpes, cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[4], maxDrawdowns, cancellationToken: ct);
        await rowGroup.WriteAsync<double>(fields[5], winRates, cancellationToken: ct);
        await rowGroup.WriteAsync<int>(fields[6], totalTrades, cancellationToken: ct);

        for (var i = 0; i < parameterNames.Length; i++)
        {
            var name = parameterNames[i];
            var values = new string[runs.Length];
            for (var j = 0; j < runs.Length; j++)
                values[j] = GetParameterValue(runs[j], name)?.ToString() ?? "";

            ct.ThrowIfCancellationRequested();
            await rowGroup.WriteAsync(fields[7 + i], values);
        }
    }

    private StrategyRunResult[] CopyRunsByVariantIndex()
    {
        var runs = new StrategyRunResult[_result.Runs.Count];
        for (var i = 0; i < runs.Length; i++)
            runs[i] = _result.Runs[i];

        for (var i = 1; i < runs.Length; i++)
        {
            var run = runs[i];
            var j = i - 1;
            while (j >= 0 && runs[j].VariantIndex > run.VariantIndex)
            {
                runs[j + 1] = runs[j];
                j--;
            }

            runs[j + 1] = run;
        }

        return runs;
    }

    private IReadOnlyList<StrategyRunResult> TopBy(
        Func<StrategyRunResult, double> metric,
        int count,
        bool descending)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0 || _result.Runs.Count == 0)
            return [];

        var resultCount = Math.Min(count, _result.Runs.Count);
        var selected = new StrategyRunResult[resultCount];
        var selectedMetrics = new double[resultCount];
        var selectedCount = 0;
        var worstIndex = 0;

        for (var i = 0; i < _result.Runs.Count; i++)
        {
            var run = _result.Runs[i];
            var value = metric(run);
            if (selectedCount < resultCount)
            {
                selected[selectedCount] = run;
                selectedMetrics[selectedCount] = value;
                selectedCount++;
                worstIndex = FindWorst(selectedMetrics, selectedCount, descending);
                continue;
            }

            if (!IsBetter(value, selectedMetrics[worstIndex], descending))
                continue;

            selected[worstIndex] = run;
            selectedMetrics[worstIndex] = value;
            worstIndex = FindWorst(selectedMetrics, selectedCount, descending);
        }

        SortSelected(selected, selectedMetrics, selectedCount, descending);
        return selected;
    }

    private static int FindWorst(double[] metrics, int count, bool descending)
    {
        var worst = 0;
        for (var i = 1; i < count; i++)
        {
            if (IsWorse(metrics[i], metrics[worst], descending))
                worst = i;
        }

        return worst;
    }

    private static bool IsBetter(double candidate, double current, bool descending)
        => descending ? candidate > current : candidate < current;

    private static bool IsWorse(double candidate, double current, bool descending)
        => descending ? candidate < current : candidate > current;

    private static void SortSelected(
        StrategyRunResult[] runs,
        double[] metrics,
        int count,
        bool descending)
    {
        for (var i = 1; i < count; i++)
        {
            var run = runs[i];
            var metric = metrics[i];
            var j = i - 1;
            while (j >= 0 && IsBetter(metric, metrics[j], descending))
            {
                runs[j + 1] = runs[j];
                metrics[j + 1] = metrics[j];
                j--;
            }

            runs[j + 1] = run;
            metrics[j + 1] = metric;
        }
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
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _result.Runs.Count; i++)
            _result.Runs[i].Parameters.AddNamesTo(names, seen);

        names.Sort(StringComparer.Ordinal);
        return names.ToArray();
    }

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
        return run.Parameters.TryGet(name, out var value) ? value : null;
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
