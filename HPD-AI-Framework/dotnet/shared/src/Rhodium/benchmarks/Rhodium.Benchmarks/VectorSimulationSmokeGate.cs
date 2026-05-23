using System.Diagnostics;
using System.Text.Json;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Attributes;
using Rhodium.Primitives;
using Rhodium.Simulation;
using Rhodium.Tensor;

namespace Rhodium.Benchmarks;

public static class VectorSimulationSmokeGate
{
    private const int ReportVersion = 1;
    private const string GateName = "vector-smoke";
    private const int VariantCount = 10_000;
    private const int BarCount = 100;
    private static readonly TimeSpan MaxElapsed = TimeSpan.FromMinutes(5);

    public static int Run(IReadOnlyList<string> args)
    {
        var reportPath = GetOptionValue(args, "--vector-smoke-report");
        var certificationRunId = GetOptionValue(args, "--certification-run-id") ?? Guid.NewGuid().ToString("N");
        var maxDegreeOfParallelism = Environment.ProcessorCount;
        var history = SharedHistory.Load(CreateBars(BarCount));
        var grid = ParameterGrid.Create()
            .Add(nameof(SmokeVectorStrategy.Threshold), Enumerable.Range(0, VariantCount).ToArray());

        var stopwatch = Stopwatch.StartNew();
        var result = Rhodium.Simulation.Rhodium.Simulate<SmokeVectorStrategy>()
            .WithHistory(history)
            .WithGrid(grid)
            .WithFidelity(SimulationFidelity.Vector)
            .WithMaxDegreeOfParallelism(maxDegreeOfParallelism)
            .Run();
        stopwatch.Stop();

        var passed = true;
        string? failure = null;
        if (result.Runs.Count != VariantCount)
        {
            failure = $"Expected {VariantCount} runs, got {result.Runs.Count}.";
            passed = false;
        }
        else if (result.Batch.TotalReturn.Length != VariantCount)
        {
            failure = $"Expected batch length {VariantCount}, got {result.Batch.TotalReturn.Length}.";
            passed = false;
        }
        else if (stopwatch.Elapsed > MaxElapsed)
        {
            failure = $"Vector smoke gate exceeded {MaxElapsed.TotalSeconds:n0}s: {stopwatch.Elapsed}.";
            passed = false;
        }

        if (reportPath is not null)
            WriteReport(reportPath, certificationRunId, stopwatch.Elapsed, maxDegreeOfParallelism, passed, failure);

        if (!passed)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }

        Console.WriteLine($"Vector smoke gate passed: {VariantCount:n0} variants x {BarCount:n0} bars in {stopwatch.Elapsed} using {maxDegreeOfParallelism:n0} logical processors.");
        if (reportPath is not null)
            Console.WriteLine($"Vector smoke report: {reportPath}");
        return 0;
    }

    private static string? GetOptionValue(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void WriteReport(
        string path,
        string certificationRunId,
        TimeSpan elapsed,
        int maxDegreeOfParallelism,
        bool passed,
        string? failure)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var report = new VectorSmokeReport(
            ReportVersion,
            GateName,
            certificationRunId,
            SmokeReportEnvironment.Create(),
            VariantCount,
            BarCount,
            MaxElapsed,
            elapsed,
            Environment.ProcessorCount,
            maxDegreeOfParallelism,
            passed,
            failure);

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<FinanceEvent> CreateBars(int count)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        for (var i = 0; i < count; i++)
        {
            var close = 100m + i;
            var bar = new Bar(
                new Price(close, Currency.USD),
                new Price(close + 1m, Currency.USD),
                new Price(close - 1m, Currency.USD),
                new Price(close, Currency.USD),
                new Qty(10_000m),
                default,
                Duration.FromMinutes(1));
            yield return new BarClosed(instrument, bar);
        }
    }

    private sealed record VectorSmokeReport(
        int ReportVersion,
        string GateName,
        string CertificationRunId,
        SmokeReportEnvironment Environment,
        int VariantCount,
        int BarCount,
        TimeSpan MaxElapsed,
        TimeSpan Elapsed,
        int LogicalProcessorCount,
        int MaxDegreeOfParallelism,
        bool Passed,
        string? Failure);

    private sealed class SmokeVectorStrategy : Strategy
    {
        private AssetId _spy;
        private double _lastSignal;

        [Param]
        public int Threshold { get; init; }

        protected override void OnInitialize(in SetupContext setup)
        {
            _spy = setup.AddEquity("SPY");
        }

        protected override void __GeneratedRunBars(in MarketKernel market, ref PortfolioContext portfolio)
        {
            var close = market.GetScalar(Field.Close, _spy);
            _lastSignal = close > Threshold ? close : 0d;
        }
    }
}
