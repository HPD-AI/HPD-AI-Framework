using Parquet;
using Rhodium.Analytics;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Tests;

public sealed class VectorScanAnalyzerTests
{
    [Fact]
    public void TopBySharpe_RanksRunsDescending()
    {
        var result = CreateResult();

        var top = result.TopBySharpe(2).ToArray();

        Assert.Equal([2, 3], top.Select(static run => run.VariantIndex).ToArray());
    }

    [Fact]
    public void Filter_AppliesRiskAndTradeConstraints()
    {
        var result = CreateResult();

        var filtered = result.Analyze().Filter(
            minSharpe: 1.0,
            maxDrawdown: 0.10m,
            minWinRate: 0.60m,
            minTrades: 10);

        var run = Assert.Single(filtered);
        Assert.Equal(2, run.VariantIndex);
    }

    [Fact]
    public void ToHeatmap_MapsMetricByParameterAxes()
    {
        var result = CreateResult();

        var heatmap = result.Analyze().ToHeatmap(
            static run => (double)run.TearSheet.TotalReturn,
            "Fast",
            "Slow");

        Assert.Equal(2, heatmap.GetLength(0));
        Assert.Equal(2, heatmap.GetLength(1));
        Assert.Equal(0.10, heatmap[0, 0]);
        Assert.Equal(0.20, heatmap[0, 1]);
        Assert.Equal(0.15, heatmap[1, 0]);
    }

    [Fact]
    public void SimulationResult_BatchMirrorsRunTearSheets()
    {
        var result = CreateResult();

        Assert.Equal([0.10, 0.20, 0.15], result.Batch.TotalReturn.ToArray());
        Assert.Equal([0.8, 1.4, 1.1], result.Batch.Sharpe.ToArray());
        Assert.Equal([0.12, 0.08, 0.18], result.Batch.MaxDrawdown.ToArray());
    }

    [Fact]
    public void ToParameterGrid_PreservesExactRunRowsInsteadOfCartesianExpansion()
    {
        var result = CreateResult();

        var grid = result.Runs.ToParameterGrid();

        Assert.Equal(3, grid.Count);
        Assert.Equal(["Fast", "Slow"], grid.ParameterNames);
        Assert.Equal(10, grid.GetParametersForVariant(0).Get<int>("Fast"));
        Assert.Equal(30, grid.GetParametersForVariant(0).Get<int>("Slow"));
        Assert.Equal(20, grid.GetParametersForVariant(1).Get<int>("Fast"));
        Assert.Equal(30, grid.GetParametersForVariant(1).Get<int>("Slow"));
        Assert.Equal(10, grid.GetParametersForVariant(2).Get<int>("Fast"));
        Assert.Equal(40, grid.GetParametersForVariant(2).Get<int>("Slow"));
    }

    [Fact]
    public void ToCsv_ExportsParametersAndMetrics()
    {
        var result = CreateResult();

        var csv = result.Analyze().ToCsv();

        Assert.Contains("strategy_id,variant_index,total_return,sharpe,max_drawdown,win_rate,total_trades,Fast,Slow", csv);
        Assert.Contains("1,1,0.10,0.8,0.12,0.55,8,10,30", csv);
        Assert.Contains("2,2,0.20,1.4,0.08,0.65,12,20,30", csv);
    }

    [Fact]
    public void ExportToCsv_CreatesParentDirectoryAndEscapesValues()
    {
        var result = CreateResultWithEscapedParameters();
        var directory = Path.Combine(Path.GetTempPath(), $"rhodium-vector-csv-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nested", "scan.csv");

        try
        {
            result.Analyze().ExportToCsv(path);

            var csv = File.ReadAllText(path);
            Assert.Contains("\"Fast,Name\"", csv);
            Assert.Contains("\"10,\"\"quoted\"\"\"", csv);
            Assert.Contains("\"multi\nline\"", csv);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportToParquet_WritesReadableRunMetricsAndParameters()
    {
        var result = CreateResult();
        var path = Path.Combine(Path.GetTempPath(), $"rhodium-vector-scan-{Guid.NewGuid():N}.parquet");

        try
        {
            result.Analyze().ExportToParquet(path);

            await using var stream = File.OpenRead(path);
            await using var reader = await ParquetReader.CreateAsync(stream);
            Assert.Equal(1, reader.RowGroupCount);

            var fields = reader.Schema.GetDataFields();
            Assert.Equal(
                ["strategy_id", "variant_index", "total_return", "sharpe", "max_drawdown", "win_rate", "total_trades", "param_Fast", "param_Slow"],
                fields.Select(static field => field.Name).ToArray());

            using var rowGroup = reader.OpenRowGroupReader(0);
            Assert.Equal(3, rowGroup.RowCount);

            var variants = new int[rowGroup.RowCount];
            await rowGroup.ReadAsync<int>(fields[1], variants);
            Assert.Equal([1, 2, 3], variants);

            var returns = new double[rowGroup.RowCount];
            await rowGroup.ReadAsync<double>(fields[2], returns);
            Assert.Equal([0.10d, 0.20d, 0.15d], returns);

            var fast = new string[rowGroup.RowCount];
            await rowGroup.ReadAsync(fields[7], fast);
            Assert.Equal(["10", "20", "10"], fast);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static SimulationResult CreateResult()
    {
        var runs = new[]
        {
            CreateRun(1, variant: 1, fast: 10, slow: 30, totalReturn: 0.10m, sharpe: 0.8m, drawdown: 0.12m, winRate: 0.55m, trades: 8),
            CreateRun(2, variant: 2, fast: 20, slow: 30, totalReturn: 0.20m, sharpe: 1.4m, drawdown: 0.08m, winRate: 0.65m, trades: 12),
            CreateRun(3, variant: 3, fast: 10, slow: 40, totalReturn: 0.15m, sharpe: 1.1m, drawdown: 0.18m, winRate: 0.70m, trades: 20)
        };

        return new SimulationResult(
            runs,
            BatchTearSheetBuilder.FromTearSheets(runs.Select(static run => run.TearSheet).ToArray()),
            [],
            []);
    }

    private static SimulationResult CreateResultWithEscapedParameters()
    {
        var runs = new[]
        {
            new StrategyRunResult(
                new StrategyId(1),
                1,
                new ParameterSet(new Dictionary<string, object>
                {
                    ["Fast,Name"] = "10,\"quoted\"",
                    ["Note"] = "multi\nline"
                }),
                CreateTearSheet(0.10m, 0.8m, 0.12m, 0.55m, 8),
                new PortfolioSnapshot { StrategyId = new StrategyId(1) })
        };

        return new SimulationResult(
            runs,
            BatchTearSheetBuilder.FromTearSheets(runs.Select(static run => run.TearSheet).ToArray()),
            [],
            []);
    }

    private static StrategyRunResult CreateRun(
        int strategyId,
        int variant,
        int fast,
        int slow,
        decimal totalReturn,
        decimal sharpe,
        decimal drawdown,
        decimal winRate,
        int trades)
        => new(
            new StrategyId(strategyId),
            variant,
            new ParameterSet(new Dictionary<string, object>
            {
                ["Fast"] = fast,
                ["Slow"] = slow
            }),
            CreateTearSheet(totalReturn, sharpe, drawdown, winRate, trades),
            new PortfolioSnapshot { StrategyId = new StrategyId(strategyId) });

    private static TearSheet CreateTearSheet(
        decimal totalReturn,
        decimal sharpe,
        decimal drawdown,
        decimal winRate,
        int trades)
        => new(
            TotalReturn: totalReturn,
            Cagr: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: sharpe,
            SortinoRatio: 0m,
            CalmarRatio: 0m,
            MaxDrawdown: drawdown,
            MaxDrawdownDuration: Duration.Zero,
            WinRate: winRate,
            ProfitFactor: 0m,
            PayoffRatio: 0m,
            ExpectancyPerTrade: 0m,
            TotalTrades: trades,
            WinningTrades: 0,
            LosingTrades: 0,
            BreakevenTrades: 0,
            TotalPnL: Money.Zero(Currency.USD),
            GrossPnL: Money.Zero(Currency.USD),
            TotalCommissions: Money.Zero(Currency.USD),
            AvgWin: Money.Zero(Currency.USD),
            AvgLoss: Money.Zero(Currency.USD),
            LargestWin: Money.Zero(Currency.USD),
            LargestLoss: Money.Zero(Currency.USD),
            AvgHoldingPeriod: Duration.Zero,
            AvgWinHoldingPeriod: Duration.Zero,
            AvgLossHoldingPeriod: Duration.Zero,
            Period: new DateRange(default, default));
}
