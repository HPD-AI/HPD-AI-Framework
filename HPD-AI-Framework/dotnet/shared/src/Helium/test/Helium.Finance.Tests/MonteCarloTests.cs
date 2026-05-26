using Helium.Finance.MonteCarlo;
using Helium.Finance.Options;
using Helium.Finance.Processes;

namespace Helium.Finance.Tests;

public class MonteCarloTests
{
    [Fact]
    public void NormalRandomGeneratorIsDeterministicForSeed()
    {
        var first = new NormalRandomGenerator(12345);
        var second = new NormalRandomGenerator(12345);

        for (var i = 0; i < 20; i++)
            AssertClose(first.NextStandardNormal(), second.NextStandardNormal(), 0.0);
    }

    [Fact]
    public void NormalRandomGeneratorProducesFiniteVariates()
    {
        var generator = new NormalRandomGenerator(12345);

        for (var i = 0; i < 1_000; i++)
            Assert.True(double.IsFinite(generator.NextStandardNormal()));
    }

    [Fact]
    public void HaltonSequenceMatchesKnownBaseTwoValues()
    {
        var sequence = new HaltonSequence(@base: 2);

        AssertClose(0.5, sequence.Next(), 0.0);
        AssertClose(0.25, sequence.Next(), 0.0);
        AssertClose(0.75, sequence.Next(), 0.0);
        AssertClose(0.125, sequence.Next(), 0.0);
    }

    [Fact]
    public void HaltonSequenceValuesStayInsideOpenUnitInterval()
    {
        var sequence = new HaltonSequence(@base: 2);

        for (var i = 0; i < 1_000; i++)
        {
            var value = sequence.Next();
            Assert.True(double.IsFinite(value), $"Halton value {value:R} must be finite.");
            Assert.True(value > 0.0 && value < 1.0, $"Halton value {value:R} must be inside (0, 1).");
        }
    }

    [Fact]
    public void HaltonSequenceRejectsIndexOverflow()
    {
        var sequence = new HaltonSequence(@base: 2, startIndex: int.MaxValue - 1);

        Assert.True(double.IsFinite(sequence.Next()));
        Assert.Throws<InvalidOperationException>(() => sequence.Next());
    }

    [Fact]
    public void LowDiscrepancyNormalGeneratorIsDeterministic()
    {
        var first = new LowDiscrepancyNormalGenerator();
        var second = new LowDiscrepancyNormalGenerator();

        for (var i = 0; i < 20; i++)
            AssertClose(first.NextStandardNormal(), second.NextStandardNormal(), 0.0);
    }

    [Fact]
    public void LowDiscrepancyNormalGeneratorProducesFiniteVariates()
    {
        var generator = new LowDiscrepancyNormalGenerator();

        for (var i = 0; i < 1_000; i++)
            Assert.True(double.IsFinite(generator.NextStandardNormal()));
    }

    [Fact]
    public void MonteCarloEstimateRejectsImpossibleStates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloEstimate(double.NaN, 0.0, 0.0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloEstimate(1.0, -1e-6, 0.0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloEstimate(1.0, 0.0, double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloEstimate(1.0, 0.0, 0.0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloEstimate(double.MaxValue, 0.0, double.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloEstimate(1.0, 0.0, 1e-6, 1));
    }

    [Fact]
    public void MonteCarloEstimateComputesBoundsFromConfidenceRadius()
    {
        var estimate = new MonteCarloEstimate(10.0, 0.25, 0.75, 100);

        AssertClose(9.25, estimate.LowerBound, 0.0);
        AssertClose(10.75, estimate.UpperBound, 0.0);
    }

    [Fact]
    public void GeometricBrownianMotionZeroShockMatchesDriftAdjustedEvolution()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 0.05, 0.20);
        var actual = process.Evolve(100.0, 1.25, 0.0);
        var expected = 100.0 * Math.Exp((0.05 - 0.5 * 0.20 * 0.20) * 1.25);

        AssertClose(expected, actual, 1e-12);
    }

    [Fact]
    public void GeometricBrownianMotionExposesQuantLibInstantaneousSemantics()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 0.05, 0.20);

        AssertClose(100.0, process.X0(), 0.0);
        AssertClose(0.05 * 125.0, process.InstantaneousDrift(time: 0.75, value: 125.0), 1e-15);
        AssertClose(0.20 * 125.0, process.InstantaneousDiffusion(time: 0.75, value: 125.0), 1e-15);
    }

    [Fact]
    public void GeometricBrownianMotionRejectsNonfiniteInstantaneousOutputs()
    {
        var driftOverflow = new GeometricBrownianMotionProcess(double.MaxValue, double.MaxValue, 0.20);
        var diffusionOverflow = new GeometricBrownianMotionProcess(double.MaxValue, 0.05, double.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(() => driftOverflow.InstantaneousDrift(time: 0.0, value: double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => diffusionOverflow.InstantaneousDiffusion(time: 0.0, value: double.MaxValue));
    }

    [Fact]
    public void GeometricBrownianMotionAnalyticMomentsMatchLognormalFormula()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 0.05, 0.20);
        const double time = 1.25;
        var expectedMean = 100.0 * Math.Exp(0.05 * time);
        var expectedVariance = 100.0 * 100.0 *
            Math.Exp(2.0 * 0.05 * time) *
            (Math.Exp(0.20 * 0.20 * time) - 1.0);

        AssertClose(expectedMean, process.ExpectedValue(time), 1e-12);
        AssertClose(expectedVariance, process.Variance(time), 1e-10);
        AssertClose(Math.Sqrt(expectedVariance), process.StandardDeviation(time), 1e-12);
    }

    [Fact]
    public void GeometricBrownianMotionRejectsInvalidProcessParameters()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 0.05, 0.20);

        Assert.Throws<ArgumentOutOfRangeException>(() => new GeometricBrownianMotionProcess(double.NaN, 0.05, 0.20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeometricBrownianMotionProcess(100.0, double.NaN, 0.20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeometricBrownianMotionProcess(100.0, 0.05, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => process with { InitialValue = -1.0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => process with { Volatility = -0.01 });
    }

    [Fact]
    public void GeometricBrownianMotionEvolveRejectsNonfiniteOutput()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 1_000.0, 0.20);

        Assert.Throws<ArgumentOutOfRangeException>(() => process.Evolve(100.0, 1.0, 0.0));
    }

    [Fact]
    public void GeometricBrownianMotionMomentsRejectNonfiniteOutputs()
    {
        var explosiveDrift = new GeometricBrownianMotionProcess(100.0, 1_000.0, 0.20);
        var explosiveVariance = new GeometricBrownianMotionProcess(100.0, 0.05, 1_000.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => explosiveDrift.ExpectedValue(1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => explosiveVariance.Variance(1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => explosiveVariance.StandardDeviation(1.0));
    }

    [Fact]
    public void PathGeneratorWritesInitialValueAndFinitePath()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 0.03, 0.18);
        var random = new NormalRandomGenerator(777);
        Span<double> path = stackalloc double[13];

        PathGenerator.Generate(process, 1.0, path, random);

        AssertClose(100.0, path[0], 0.0);
        foreach (var value in path)
            Assert.True(double.IsFinite(value) && value >= 0.0, $"Path value {value:R} must be finite and nonnegative.");
    }

    [Fact]
    public void PathGeneratorRejectsNullRandomGenerator()
    {
        var process = new GeometricBrownianMotionProcess(100.0, 0.03, 0.18);
        var path = new double[2];

        Assert.Throws<ArgumentNullException>(() => PathGenerator.Generate(process, 1.0, path, null!));
    }

    [Fact]
    public void MonteCarloInputRejectsInvalidOptionRight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackScholesInput(
            (OptionRight)999,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.03,
            DividendYield: 0.01));
    }

    [Fact]
    public void MonteCarloBlackScholesEstimateContainsClosedFormPrice()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.03,
            DividendYield: 0.01);

        var estimate = EuropeanOptionMonteCarlo.PriceBlackScholes(input, samples: 120_000, seed: 98765, confidenceLevel: 0.999);
        var closedForm = BlackScholes.Price(input);

        Assert.True(
            closedForm >= estimate.LowerBound && closedForm <= estimate.UpperBound,
            $"Closed form {closedForm:R} should be inside [{estimate.LowerBound:R}, {estimate.UpperBound:R}], estimate {estimate.Value:R}, stderr {estimate.StandardError:R}.");
    }

    [Fact]
    public void AntitheticMonteCarloEstimateContainsClosedFormPrice()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.03,
            DividendYield: 0.01);

        var estimate = EuropeanOptionMonteCarlo.PriceBlackScholesAntithetic(input, pairs: 60_000, seed: 98765, confidenceLevel: 0.999);
        var closedForm = BlackScholes.Price(input);

        Assert.True(
            closedForm >= estimate.LowerBound && closedForm <= estimate.UpperBound,
            $"Closed form {closedForm:R} should be inside [{estimate.LowerBound:R}, {estimate.UpperBound:R}], estimate {estimate.Value:R}, stderr {estimate.StandardError:R}.");
    }

    [Fact]
    public void AntitheticMonteCarloReducesStandardErrorForMatchedDrawBudget()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.03,
            DividendYield: 0.01);

        var plain = EuropeanOptionMonteCarlo.PriceBlackScholes(input, samples: 120_000, seed: 54321, confidenceLevel: 0.95);
        var antithetic = EuropeanOptionMonteCarlo.PriceBlackScholesAntithetic(input, pairs: 60_000, seed: 54321, confidenceLevel: 0.95);

        Assert.True(
            antithetic.StandardError < plain.StandardError,
            $"Expected antithetic stderr {antithetic.StandardError:R} to be below plain stderr {plain.StandardError:R}.");
    }

    [Fact]
    public void QuasiMonteCarloEstimateIsCloseToClosedFormPrice()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.03,
            DividendYield: 0.01);

        var estimate = EuropeanOptionMonteCarlo.PriceBlackScholesQuasiRandom(input, samples: 16_384);
        var closedForm = BlackScholes.Price(input);

        AssertClose(closedForm, estimate.Value, 0.03);
        Assert.Equal(16_384, estimate.Samples);
    }

    [Fact]
    public void QuasiMonteCarloRejectsInvalidStartIndex()
    {
        var input = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholesQuasiRandom(input, samples: 100, startIndex: 0));
    }

    [Fact]
    public void MonteCarloZeroTimeReturnsIntrinsicWithZeroError()
    {
        var input = new BlackScholesInput(
            OptionRight.Put,
            Spot: 95.0,
            Strike: 100.0,
            TimeToExpiry: 0.0,
            Volatility: 0.30,
            RiskFreeRate: 0.05,
            DividendYield: 0.0);

        var estimate = EuropeanOptionMonteCarlo.PriceBlackScholes(input, samples: 10, seed: 1);

        AssertClose(5.0, estimate.Value, 0.0);
        AssertClose(0.0, estimate.StandardError, 0.0);
        AssertClose(0.0, estimate.ConfidenceRadius, 0.0);
        Assert.Equal(10, estimate.Samples);
    }

    [Fact]
    public void MonteCarloRejectsNonfiniteDiscountFactor()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: -1_000.0,
            DividendYield: 0.0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholes(input, samples: 10, seed: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholesAntithetic(input, pairs: 10, seed: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholesQuasiRandom(input, samples: 10));
    }

    [Fact]
    public void MonteCarloRejectsConfidenceLevelTooCloseToOneForFiniteCriticalValue()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.03,
            DividendYield: 0.01);
        var confidenceLevel = double.BitDecrement(1.0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholes(input, samples: 10, seed: 1, confidenceLevel));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholesAntithetic(input, pairs: 10, seed: 1, confidenceLevel));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EuropeanOptionMonteCarlo.PriceBlackScholesQuasiRandom(input, samples: 10, confidenceLevel: confidenceLevel));
    }

    [Fact]
    public void AntitheticMonteCarloZeroTimeReturnsIntrinsicWithZeroError()
    {
        var input = new BlackScholesInput(
            OptionRight.Put,
            Spot: 95.0,
            Strike: 100.0,
            TimeToExpiry: 0.0,
            Volatility: 0.30,
            RiskFreeRate: 0.05,
            DividendYield: 0.0);

        var estimate = EuropeanOptionMonteCarlo.PriceBlackScholesAntithetic(input, pairs: 10, seed: 1);

        AssertClose(5.0, estimate.Value, 0.0);
        AssertClose(0.0, estimate.StandardError, 0.0);
        AssertClose(0.0, estimate.ConfidenceRadius, 0.0);
        Assert.Equal(10, estimate.Samples);
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
