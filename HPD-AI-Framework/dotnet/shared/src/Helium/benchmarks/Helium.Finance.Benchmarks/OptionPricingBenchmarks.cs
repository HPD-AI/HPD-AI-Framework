using BenchmarkDotNet.Attributes;
using Helium.Finance.Curves;
using Helium.Finance.MonteCarlo;
using Helium.Finance.Options;
using Helium.Finance.Processes;
using Helium.Finance.Scenarios;
using Helium.Finance.Volatility;

namespace Helium.Finance.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class OptionPricingBenchmarks
{
    private Black76Input _black76;
    private BlackScholesInput _blackScholes;
    private GeometricBrownianMotionProcess _gbmProcess;
    private BachelierInput _bachelier;
    private BinomialTreeInput _europeanTree;
    private BinomialTreeInput _americanTree;
    private TrinomialTreeInput _europeanTrinomialTree;
    private TrinomialTreeInput _americanTrinomialTree;
    private OptionScenarioShock _scenarioShock;
    private OptionScenarioShock[] _scenarioShocks = [];
    private OptionScenarioResult[] _scenarioResults = [];
    private Black76VolatilityQuote[] _volatilityQuotes = [];
    private Black76VolatilityQuote[] _surfaceVolatilityQuotes = [];
    private BachelierVolatilityQuote[] _normalVolatilityQuotes = [];
    private Black76Input[] _batch = [];
    private double[] _batchOutput = [];
    private BlackScholesInput[] _blackScholesBatch = [];
    private double[] _blackScholesBatchOutput = [];
    private BachelierInput[] _bachelierBatch = [];
    private double[] _bachelierBatchOutput = [];
    private double _black76MarketPrice;
    private double _bachelierMarketPrice;

    [Params(1_000, 100_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _black76 = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97);
        _blackScholes = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01);
        _gbmProcess = new GeometricBrownianMotionProcess(100.0, 0.03 - 0.01, 0.20);
        _bachelier = new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 20.0, 0.97);
        _europeanTree = new BinomialTreeInput(OptionRight.Call, ExerciseStyle.European, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01, 1_000);
        _americanTree = new BinomialTreeInput(OptionRight.Put, ExerciseStyle.American, 95.0, 105.0, 1.0, 0.25, 0.05, 0.0, 1_000);
        _europeanTrinomialTree = new TrinomialTreeInput(OptionRight.Call, ExerciseStyle.European, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01, 1_000);
        _americanTrinomialTree = new TrinomialTreeInput(OptionRight.Put, ExerciseStyle.American, 95.0, 105.0, 1.0, 0.25, 0.05, 0.0, 1_000);
        _scenarioShock = new OptionScenarioShock(UnderlyingRelativeShift: 0.02, VolatilityAbsoluteShift: 0.01, RiskFreeRateAbsoluteShift: 0.0025);
        _scenarioShocks =
        [
            new OptionScenarioShock(UnderlyingRelativeShift: -0.10, VolatilityAbsoluteShift: 0.03),
            new OptionScenarioShock(UnderlyingRelativeShift: -0.05, VolatilityAbsoluteShift: 0.01),
            new OptionScenarioShock(),
            new OptionScenarioShock(UnderlyingRelativeShift: 0.05, VolatilityAbsoluteShift: -0.01),
            new OptionScenarioShock(UnderlyingRelativeShift: 0.10, VolatilityAbsoluteShift: -0.02)
        ];
        _scenarioResults = new OptionScenarioResult[_scenarioShocks.Length];
        _black76MarketPrice = Black76.Price(_black76);
        _bachelierMarketPrice = Bachelier.Price(_bachelier);
        _volatilityQuotes =
        [
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 0.5, 0.22, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.96)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 110.0, 1.5, 0.28, 0.94))
        ];
        _surfaceVolatilityQuotes =
        [
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 0.5, 0.22, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 0.5, 0.20, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 110.0, 0.5, 0.23, 0.98)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 1.0, 0.27, 0.96)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.96)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 110.0, 1.0, 0.28, 0.96)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 90.0, 2.0, 0.31, 0.94)),
            QuoteFrom(new Black76Input(OptionRight.Call, 100.0, 100.0, 2.0, 0.30, 0.94)),
            QuoteFrom(new Black76Input(OptionRight.Put, 100.0, 110.0, 2.0, 0.32, 0.94))
        ];
        _normalVolatilityQuotes =
        [
            QuoteFrom(new BachelierInput(OptionRight.Call, 100.0, 90.0, 0.5, 18.0, 0.98)),
            QuoteFrom(new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 21.0, 0.96)),
            QuoteFrom(new BachelierInput(OptionRight.Put, 100.0, 110.0, 1.5, 24.0, 0.94))
        ];
        _batch = Enumerable.Range(0, BatchSize)
            .Select(i => new Black76Input(
                i % 2 == 0 ? OptionRight.Call : OptionRight.Put,
                Forward: 95.0 + i % 17,
                Strike: 100.0,
                TimeToExpiry: 0.25 + (i % 12) / 12.0,
                Volatility: 0.15 + (i % 10) * 0.01,
                DiscountFactor: 0.97))
            .ToArray();
        _batchOutput = new double[BatchSize];
        _blackScholesBatch = Enumerable.Range(0, BatchSize)
            .Select(i => new BlackScholesInput(
                i % 2 == 0 ? OptionRight.Call : OptionRight.Put,
                Spot: 95.0 + i % 17,
                Strike: 100.0,
                TimeToExpiry: 0.25 + (i % 12) / 12.0,
                Volatility: 0.15 + (i % 10) * 0.01,
                RiskFreeRate: 0.03,
                DividendYield: 0.01))
            .ToArray();
        _blackScholesBatchOutput = new double[BatchSize];
        _bachelierBatch = Enumerable.Range(0, BatchSize)
            .Select(i => new BachelierInput(
                i % 2 == 0 ? OptionRight.Call : OptionRight.Put,
                Forward: 95.0 + i % 17,
                Strike: 100.0,
                TimeToExpiry: 0.25 + (i % 12) / 12.0,
                NormalVolatility: 12.0 + i % 11,
                DiscountFactor: 0.97))
            .ToArray();
        _bachelierBatchOutput = new double[BatchSize];
    }

    [Benchmark]
    public double ScalarBlack76Price() => Black76.Price(_black76);

    [Benchmark]
    public OptionGreeks ScalarBlack76PriceAndGreeks() => Black76.PriceAndGreeks(_black76);

    [Benchmark]
    public FiniteDifferenceGreekEstimate Black76FiniteDifferenceGreeks() =>
        GreekFiniteDifferences.EstimateBlack76(_black76);

    [Benchmark]
    public double ScalarBlackScholesPrice() => BlackScholes.Price(_blackScholes);

    [Benchmark]
    public double GeometricBrownianMotionMoments() =>
        _gbmProcess.ExpectedValue(1.0) + _gbmProcess.StandardDeviation(1.0);

    [Benchmark]
    public OptionScenarioResult BlackScholesScenario() =>
        OptionScenarioEvaluator.EvaluateBlackScholes(_blackScholes, _scenarioShock);

    [Benchmark]
    public double BlackScholesScenarioGrid()
    {
        OptionScenarioGrid.EvaluateBlackScholes(_blackScholes, _scenarioShocks, _scenarioResults);
        var sum = 0.0;
        foreach (var result in _scenarioResults)
            sum += result.PriceChange;
        return sum;
    }

    [Benchmark]
    public double ScalarBachelierPrice() => Bachelier.Price(_bachelier);

    [Benchmark]
    public ImpliedVolatilityResult BachelierImpliedVolatility() =>
        Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(_bachelier.Right, _bachelier.Forward, _bachelier.Strike, _bachelier.TimeToExpiry, _bachelier.DiscountFactor),
            _bachelierMarketPrice,
            new ImpliedVolatilityOptions(UpperVolatility: 100.0));

    [Benchmark]
    public double EuropeanBinomialTreePrice() => BinomialTree.Price(_europeanTree);

    [Benchmark]
    public double AmericanBinomialTreePrice() => BinomialTree.Price(_americanTree);

    [Benchmark]
    public double EuropeanTrinomialTreePrice() => TrinomialTree.Price(_europeanTrinomialTree);

    [Benchmark]
    public double AmericanTrinomialTreePrice() => TrinomialTree.Price(_americanTrinomialTree);

    [Benchmark]
    public MonteCarloEstimate EuropeanMonteCarloPrice() =>
        EuropeanOptionMonteCarlo.PriceBlackScholes(_blackScholes, samples: 100_000, seed: 123456, confidenceLevel: 0.95);

    [Benchmark]
    public MonteCarloEstimate EuropeanAntitheticMonteCarloPrice() =>
        EuropeanOptionMonteCarlo.PriceBlackScholesAntithetic(_blackScholes, pairs: 50_000, seed: 123456, confidenceLevel: 0.95);

    [Benchmark]
    public MonteCarloEstimate EuropeanQuasiMonteCarloPrice() =>
        EuropeanOptionMonteCarlo.PriceBlackScholesQuasiRandom(_blackScholes, samples: 100_000, startIndex: 1, confidenceLevel: 0.95);

    [Benchmark]
    public ImpliedVolatilityResult Black76ImpliedVolatility() =>
        Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(_black76.Right, _black76.Forward, _black76.Strike, _black76.TimeToExpiry, _black76.DiscountFactor),
            _black76MarketPrice);

    [Benchmark]
    public OptionPriceValidationResult Black76PriceValidation() =>
        OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(_black76.Right, _black76.Forward, _black76.Strike, _black76.TimeToExpiry, _black76.DiscountFactor),
            _black76MarketPrice);

    [Benchmark]
    public VolatilityCalibrationResult Black76VolatilityCalibration() =>
        ImpliedVolatilityCalibration.CalibrateBlack76(_volatilityQuotes);

    [Benchmark]
    public double Black76CalibratedVolatilitySurface()
    {
        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(_surfaceVolatilityQuotes);
        var surface = calibration.ToBlackVolatilitySurface(extrapolationPolicy: ExtrapolationPolicy.Flat);
        return surface.Volatility(0.75, 100.0) + surface.Volatility(1.5, 105.0);
    }

    [Benchmark]
    public double Black76CalibratedVarianceSurface()
    {
        var calibration = ImpliedVolatilityCalibration.CalibrateBlack76(_surfaceVolatilityQuotes);
        var surface = calibration.ToBlackVarianceSurface(extrapolationPolicy: ExtrapolationPolicy.Flat);
        return surface.Variance(0.75, 100.0) + surface.ForwardVolatility(0.5, 2.0, 105.0);
    }

    [Benchmark]
    public VolatilityCalibrationResult BachelierVolatilityCalibration() =>
        ImpliedVolatilityCalibration.CalibrateBachelier(_normalVolatilityQuotes, new ImpliedVolatilityOptions(UpperVolatility: 100.0));

    [Benchmark]
    public double BatchBlack76ScalarLoop()
    {
        var sum = 0.0;
        foreach (var input in _batch)
            sum += Black76.Price(input);
        return sum;
    }

    [Benchmark]
    public double BatchBlack76DestinationSpan()
    {
        Black76.BatchPrice(_batch, _batchOutput);
        var sum = 0.0;
        foreach (var price in _batchOutput)
            sum += price;
        return sum;
    }

    [Benchmark]
    public double BatchBlackScholesDestinationSpan()
    {
        BlackScholes.BatchPrice(_blackScholesBatch, _blackScholesBatchOutput);
        var sum = 0.0;
        foreach (var price in _blackScholesBatchOutput)
            sum += price;
        return sum;
    }

    [Benchmark]
    public double BatchBachelierDestinationSpan()
    {
        Bachelier.BatchPrice(_bachelierBatch, _bachelierBatchOutput);
        var sum = 0.0;
        foreach (var price in _bachelierBatchOutput)
            sum += price;
        return sum;
    }

    private static Black76VolatilityQuote QuoteFrom(Black76Input input) =>
        new(
            input.Right,
            input.Forward,
            input.Strike,
            input.TimeToExpiry,
            input.DiscountFactor,
            Black76.Price(input));

    private static BachelierVolatilityQuote QuoteFrom(BachelierInput input) =>
        new(
            input.Right,
            input.Forward,
            input.Strike,
            input.TimeToExpiry,
            input.DiscountFactor,
            Bachelier.Price(input));
}
