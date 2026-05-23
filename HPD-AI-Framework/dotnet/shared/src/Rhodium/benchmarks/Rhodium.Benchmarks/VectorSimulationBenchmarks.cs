using BenchmarkDotNet.Attributes;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Attributes;
using Rhodium.Primitives;
using Rhodium.Simulation;
using Rhodium.Tensor;

namespace Rhodium.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class VectorSimulationBenchmarks
{
    private SharedHistory _history = null!;
    private ParameterGrid _grid = null!;

    [Params(1_000, 10_000)]
    public int VariantCount { get; set; }

    [Params(100)]
    public int BarCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _history = SharedHistory.Load(CreateBars(BarCount));
        _grid = ParameterGrid.Create()
            .Add(nameof(VectorNoOrderStrategy.Threshold), Enumerable.Range(0, VariantCount).ToArray());
    }

    [Benchmark]
    public SimulationResult EventMajorVectorReplay()
        => Rhodium.Simulation.Rhodium.Simulate<VectorNoOrderStrategy>()
            .WithHistory(_history)
            .WithGrid(_grid)
            .WithFidelity(SimulationFidelity.Vector)
            .WithMaxDegreeOfParallelism(Environment.ProcessorCount)
            .Run();

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

    public sealed class VectorNoOrderStrategy : Strategy
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
