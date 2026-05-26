using Helium.Finance.Options;
using Helium.Finance.Processes;

namespace Helium.Finance.MonteCarlo;

public static class EuropeanOptionMonteCarlo
{
    public static MonteCarloEstimate PriceBlackScholes(
        BlackScholesInput input,
        int samples,
        int seed,
        double confidenceLevel = 0.95)
    {
        Validate(input, samples, confidenceLevel);

        if (input.TimeToExpiry == 0.0)
        {
            var intrinsic = Intrinsic(input.Right, input.Spot, input.Strike);
            return new MonteCarloEstimate(intrinsic, 0.0, 0.0, samples);
        }

        var process = new GeometricBrownianMotionProcess(
            input.Spot,
            input.RiskFreeRate - input.DividendYield,
            input.Volatility);
        var random = new NormalRandomGenerator(seed);
        var discountFactor = DiscountFactor(input.RiskFreeRate, input.TimeToExpiry);
        var sum = 0.0;
        var sumSquares = 0.0;

        for (var i = 0; i < samples; i++)
        {
            var terminal = process.Evolve(input.Spot, input.TimeToExpiry, random.NextStandardNormal());
            var discountedPayoff = discountFactor * Intrinsic(input.Right, terminal, input.Strike);
            AccumulatePayoff(discountedPayoff, ref sum, ref sumSquares);
        }

        var mean = sum / samples;
        var variance = samples > 1
            ? Math.Max((sumSquares - samples * mean * mean) / (samples - 1), 0.0)
            : 0.0;
        var standardError = Math.Sqrt(variance / samples);
        var confidenceRadius = NormalCriticalValue(confidenceLevel) * standardError;

        return new MonteCarloEstimate(mean, standardError, confidenceRadius, samples);
    }

    public static MonteCarloEstimate PriceBlackScholesAntithetic(
        BlackScholesInput input,
        int pairs,
        int seed,
        double confidenceLevel = 0.95)
    {
        Validate(input, pairs, confidenceLevel);

        if (input.TimeToExpiry == 0.0)
        {
            var intrinsic = Intrinsic(input.Right, input.Spot, input.Strike);
            return new MonteCarloEstimate(intrinsic, 0.0, 0.0, pairs);
        }

        var process = new GeometricBrownianMotionProcess(
            input.Spot,
            input.RiskFreeRate - input.DividendYield,
            input.Volatility);
        var random = new NormalRandomGenerator(seed);
        var discountFactor = DiscountFactor(input.RiskFreeRate, input.TimeToExpiry);
        var sum = 0.0;
        var sumSquares = 0.0;

        for (var i = 0; i < pairs; i++)
        {
            var shock = random.NextStandardNormal();
            var positiveTerminal = process.Evolve(input.Spot, input.TimeToExpiry, shock);
            var negativeTerminal = process.Evolve(input.Spot, input.TimeToExpiry, -shock);
            var positivePayoff = discountFactor * Intrinsic(input.Right, positiveTerminal, input.Strike);
            var negativePayoff = discountFactor * Intrinsic(input.Right, negativeTerminal, input.Strike);
            var pairedPayoff = 0.5 * (positivePayoff + negativePayoff);

            AccumulatePayoff(pairedPayoff, ref sum, ref sumSquares);
        }

        var mean = sum / pairs;
        var variance = pairs > 1
            ? Math.Max((sumSquares - pairs * mean * mean) / (pairs - 1), 0.0)
            : 0.0;
        var standardError = Math.Sqrt(variance / pairs);
        var confidenceRadius = NormalCriticalValue(confidenceLevel) * standardError;

        return new MonteCarloEstimate(mean, standardError, confidenceRadius, pairs);
    }

    public static MonteCarloEstimate PriceBlackScholesQuasiRandom(
        BlackScholesInput input,
        int samples,
        int startIndex = 1,
        double confidenceLevel = 0.95)
    {
        Validate(input, samples, confidenceLevel);

        if (startIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be positive.");

        if (input.TimeToExpiry == 0.0)
        {
            var intrinsic = Intrinsic(input.Right, input.Spot, input.Strike);
            return new MonteCarloEstimate(intrinsic, 0.0, 0.0, samples);
        }

        var process = new GeometricBrownianMotionProcess(
            input.Spot,
            input.RiskFreeRate - input.DividendYield,
            input.Volatility);
        var generator = new LowDiscrepancyNormalGenerator(startIndex);
        var discountFactor = DiscountFactor(input.RiskFreeRate, input.TimeToExpiry);
        var sum = 0.0;
        var sumSquares = 0.0;

        for (var i = 0; i < samples; i++)
        {
            var terminal = process.Evolve(input.Spot, input.TimeToExpiry, generator.NextStandardNormal());
            var discountedPayoff = discountFactor * Intrinsic(input.Right, terminal, input.Strike);
            AccumulatePayoff(discountedPayoff, ref sum, ref sumSquares);
        }

        var mean = sum / samples;
        var variance = samples > 1
            ? Math.Max((sumSquares - samples * mean * mean) / (samples - 1), 0.0)
            : 0.0;
        var standardError = Math.Sqrt(variance / samples);
        var confidenceRadius = NormalCriticalValue(confidenceLevel) * standardError;

        return new MonteCarloEstimate(mean, standardError, confidenceRadius, samples);
    }

    private static double Intrinsic(OptionRight right, double spot, double strike) =>
        right switch
        {
            OptionRight.Call => Math.Max(spot - strike, 0.0),
            OptionRight.Put => Math.Max(strike - spot, 0.0),
            _ => throw new ArgumentOutOfRangeException(nameof(right), right, "Unsupported option right.")
        };

    private static double DiscountFactor(double riskFreeRate, double timeToExpiry)
    {
        var discountFactor = Math.Exp(-riskFreeRate * timeToExpiry);
        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(riskFreeRate), "Monte Carlo discount factor must be finite and positive.");

        return discountFactor;
    }

    private static void AccumulatePayoff(double discountedPayoff, ref double sum, ref double sumSquares)
    {
        if (!double.IsFinite(discountedPayoff))
            throw new ArgumentOutOfRangeException(nameof(discountedPayoff), "Discounted payoff must be finite.");

        var nextSum = sum + discountedPayoff;
        if (!double.IsFinite(nextSum))
            throw new ArgumentOutOfRangeException(nameof(sum), "Monte Carlo payoff sum must be finite.");

        var payoffSquare = discountedPayoff * discountedPayoff;
        if (!double.IsFinite(payoffSquare))
            throw new ArgumentOutOfRangeException(nameof(discountedPayoff), "Discounted payoff square must be finite.");

        var nextSumSquares = sumSquares + payoffSquare;
        if (!double.IsFinite(nextSumSquares))
            throw new ArgumentOutOfRangeException(nameof(sumSquares), "Monte Carlo payoff square sum must be finite.");

        sum = nextSum;
        sumSquares = nextSumSquares;
    }

    private static double NormalCriticalValue(double confidenceLevel)
    {
        var twoSidedProbability = 0.5 + confidenceLevel / 2.0;
        if (!double.IsFinite(twoSidedProbability) || twoSidedProbability <= 0.5 || twoSidedProbability >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel), "Confidence level is too close to one for a finite two-sided normal critical value.");

        var criticalValue = Distributions.NormalDistribution.InverseCdf(twoSidedProbability);
        if (!double.IsFinite(criticalValue))
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel), "Normal critical value must be finite.");

        return criticalValue;
    }

    private static void Validate(BlackScholesInput input, int samples, double confidenceLevel)
    {
        OptionInputValidation.ValidateRight(input.Right);

        if (!double.IsFinite(input.Spot) || input.Spot < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Spot must be finite and nonnegative.");

        if (!double.IsFinite(input.Strike) || input.Strike < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Strike must be finite and nonnegative.");

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Time to expiry must be finite and nonnegative.");

        if (!double.IsFinite(input.Volatility) || input.Volatility < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Volatility must be finite and nonnegative.");

        if (!double.IsFinite(input.RiskFreeRate))
            throw new ArgumentOutOfRangeException(nameof(input), "Risk-free rate must be finite.");

        if (!double.IsFinite(input.DividendYield))
            throw new ArgumentOutOfRangeException(nameof(input), "Dividend yield must be finite.");

        if (samples <= 0)
            throw new ArgumentOutOfRangeException(nameof(samples), "Samples must be positive.");

        if (!double.IsFinite(confidenceLevel) || confidenceLevel <= 0.0 || confidenceLevel >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel), "Confidence level must be between zero and one.");
    }
}
