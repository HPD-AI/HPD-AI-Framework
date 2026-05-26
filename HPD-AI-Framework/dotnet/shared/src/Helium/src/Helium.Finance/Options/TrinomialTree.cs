namespace Helium.Finance.Options;

public static class TrinomialTree
{
    public static double Price(TrinomialTreeInput input)
    {
        Validate(input);

        if (input.TimeToExpiry == 0.0)
            return Intrinsic(input.Right, input.Spot, input.Strike);

        return input.Volatility == 0.0
            ? PriceDeterministic(input)
            : PriceLogSpaceTrinomial(input);
    }

    private static double PriceLogSpaceTrinomial(TrinomialTreeInput input)
    {
        var dt = input.TimeToExpiry / input.Steps;
        var sqrtDt = Math.Sqrt(dt);
        var dx = input.Volatility * Math.Sqrt(3.0 * dt);
        var drift = input.RiskFreeRate - input.DividendYield - 0.5 * input.Volatility * input.Volatility;
        var adjustment = drift * sqrtDt / (2.0 * input.Volatility * Math.Sqrt(3.0));
        if (!double.IsFinite(dx) || !double.IsFinite(drift) || !double.IsFinite(adjustment))
            throw new ArgumentOutOfRangeException(nameof(input), "Trinomial tree transform must be finite.");

        var probabilityUp = 1.0 / 6.0 + adjustment;
        var probabilityMiddle = 2.0 / 3.0;
        var probabilityDown = 1.0 / 6.0 - adjustment;

        if (!double.IsFinite(probabilityUp) || !double.IsFinite(probabilityDown) || probabilityUp < 0.0 || probabilityDown < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Trinomial probabilities are outside [0, 1]. Increase steps or use compatible inputs.");

        var discount = Math.Exp(-input.RiskFreeRate * dt);
        if (!double.IsFinite(discount) || discount <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Trinomial discount factor must be finite and positive.");

        var values = new double[2 * input.Steps + 1];
        var offset = input.Steps;

        for (var j = -input.Steps; j <= input.Steps; j++)
        {
            var spot = input.Spot * Math.Exp(j * dx);
            values[j + offset] = Intrinsic(input.Right, EnsureFinite(spot, nameof(input), "Trinomial terminal spot must be finite."), input.Strike);
        }

        for (var step = input.Steps - 1; step >= 0; step--)
        {
            var previousOffset = step;
            var currentOffset = step + 1;
            for (var j = -step; j <= step; j++)
            {
                var continuation = discount * (
                    probabilityDown * values[j - 1 + currentOffset] +
                    probabilityMiddle * values[j + currentOffset] +
                    probabilityUp * values[j + 1 + currentOffset]);
                continuation = EnsureFinite(continuation, nameof(input), "Trinomial continuation value must be finite.");

                values[j + previousOffset] = input.ExerciseStyle == ExerciseStyle.American
                    ? Math.Max(continuation, Intrinsic(input.Right, ExerciseSpot(input, dx, j), input.Strike))
                    : continuation;
            }
        }

        return EnsureFinite(values[0], nameof(input), "Trinomial tree price must be finite.");
    }

    private static double PriceDeterministic(TrinomialTreeInput input)
    {
        var terminalSpot = input.Spot * Math.Exp((input.RiskFreeRate - input.DividendYield) * input.TimeToExpiry);
        var discountedPayoff = Math.Exp(-input.RiskFreeRate * input.TimeToExpiry) * Intrinsic(input.Right, terminalSpot, input.Strike);
        if (!double.IsFinite(terminalSpot) || !double.IsFinite(discountedPayoff))
            throw new ArgumentOutOfRangeException(nameof(input), "Rates, dividends, and time imply a nonfinite deterministic tree value.");

        if (input.ExerciseStyle == ExerciseStyle.European)
            return discountedPayoff;

        var best = discountedPayoff;
        for (var step = 0; step <= input.Steps; step++)
        {
            var time = input.TimeToExpiry * step / input.Steps;
            var spot = input.Spot * Math.Exp((input.RiskFreeRate - input.DividendYield) * time);
            var exerciseValue = Math.Exp(-input.RiskFreeRate * time) * Intrinsic(input.Right, spot, input.Strike);
            if (!double.IsFinite(spot) || !double.IsFinite(exerciseValue))
                throw new ArgumentOutOfRangeException(nameof(input), "Rates, dividends, and time imply a nonfinite deterministic tree value.");

            best = Math.Max(best, exerciseValue);
        }

        return best;
    }

    private static double Intrinsic(OptionRight right, double spot, double strike) =>
        right switch
        {
            OptionRight.Call => Math.Max(spot - strike, 0.0),
            OptionRight.Put => Math.Max(strike - spot, 0.0),
            _ => throw new ArgumentOutOfRangeException(nameof(right), right, "Unsupported option right.")
        };

    private static double ExerciseSpot(TrinomialTreeInput input, double dx, int node)
    {
        var spot = input.Spot * Math.Exp(node * dx);
        return EnsureFinite(spot, nameof(input), "Trinomial exercise spot must be finite.");
    }

    private static void Validate(TrinomialTreeInput input)
    {
        OptionInputValidation.ValidateRight(input.Right);
        OptionInputValidation.ValidateExerciseStyle(input.ExerciseStyle);

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

        if (input.Steps < 0 || (input.TimeToExpiry > 0.0 && input.Steps == 0))
            throw new ArgumentOutOfRangeException(nameof(input), "Steps must be positive when time to expiry is positive.");
    }

    private static double EnsureFinite(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }
}
