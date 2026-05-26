namespace Helium.Finance.Options;

public static class BinomialTree
{
    public static double Price(BinomialTreeInput input)
    {
        Validate(input);

        if (input.TimeToExpiry == 0.0)
            return Intrinsic(input.Right, input.Spot, input.Strike);

        return input.Volatility == 0.0
            ? PriceDeterministic(input)
            : PriceCoxRossRubinstein(input);
    }

    private static double PriceCoxRossRubinstein(BinomialTreeInput input)
    {
        var dt = input.TimeToExpiry / input.Steps;
        var sqrtDt = Math.Sqrt(dt);
        var up = Math.Exp(input.Volatility * sqrtDt);
        var down = 1.0 / up;
        var growth = Math.Exp((input.RiskFreeRate - input.DividendYield) * dt);
        if (!double.IsFinite(up) || up <= 0.0 || !double.IsFinite(down) || down <= 0.0 || !double.IsFinite(growth) || growth < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "CRR tree transform must be finite and positive.");

        var probabilityUp = (growth - down) / (up - down);

        if (!double.IsFinite(probabilityUp) || probabilityUp < 0.0 || probabilityUp > 1.0)
            throw new ArgumentOutOfRangeException(nameof(input), "CRR risk-neutral probability is outside [0, 1]. Increase steps or use compatible inputs.");

        var discount = Math.Exp(-input.RiskFreeRate * dt);
        if (!double.IsFinite(discount) || discount <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "CRR discount factor must be finite and positive.");

        var values = new double[input.Steps + 1];

        for (var i = 0; i <= input.Steps; i++)
        {
            var spot = input.Spot * Math.Pow(up, input.Steps - i) * Math.Pow(down, i);
            values[i] = Intrinsic(input.Right, EnsureFinite(spot, nameof(input), "CRR terminal spot must be finite."), input.Strike);
        }

        for (var step = input.Steps - 1; step >= 0; step--)
        {
            for (var i = 0; i <= step; i++)
            {
                var continuation = discount * (probabilityUp * values[i] + (1.0 - probabilityUp) * values[i + 1]);
                continuation = EnsureFinite(continuation, nameof(input), "CRR continuation value must be finite.");

                values[i] = input.ExerciseStyle == ExerciseStyle.American
                    ? Math.Max(continuation, IntrinsicAtNode(input, up, down, step, i))
                    : continuation;
            }
        }

        return EnsureFinite(values[0], nameof(input), "CRR tree price must be finite.");
    }

    private static double PriceDeterministic(BinomialTreeInput input)
    {
        var dt = input.TimeToExpiry / input.Steps;
        var discount = Math.Exp(-input.RiskFreeRate * dt);
        var growth = Math.Exp((input.RiskFreeRate - input.DividendYield) * dt);
        if (!double.IsFinite(discount) || discount <= 0.0 || !double.IsFinite(growth) || growth < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Rates, dividends, and steps imply a nonfinite deterministic tree transform.");

        var values = new double[input.Steps + 1];
        var terminalSpot = input.Spot * Math.Pow(growth, input.Steps);
        if (!double.IsFinite(terminalSpot))
            throw new ArgumentOutOfRangeException(nameof(input), "Deterministic tree terminal spot must be finite.");

        Array.Fill(values, Intrinsic(input.Right, terminalSpot, input.Strike));

        for (var step = input.Steps - 1; step >= 0; step--)
        {
            var spot = input.Spot * Math.Pow(growth, step);
            if (!double.IsFinite(spot))
                throw new ArgumentOutOfRangeException(nameof(input), "Deterministic tree node spot must be finite.");

            for (var i = 0; i <= step; i++)
            {
                var continuation = discount * values[i];
                if (!double.IsFinite(continuation))
                    throw new ArgumentOutOfRangeException(nameof(input), "Deterministic tree continuation value must be finite.");

                values[i] = input.ExerciseStyle == ExerciseStyle.American
                    ? Math.Max(continuation, Intrinsic(input.Right, spot, input.Strike))
                    : continuation;
            }
        }

        return values[0];
    }

    private static double IntrinsicAtNode(BinomialTreeInput input, double up, double down, int step, int downMoves)
    {
        var spot = input.Spot * Math.Pow(up, step - downMoves) * Math.Pow(down, downMoves);
        return Intrinsic(input.Right, EnsureFinite(spot, nameof(input), "CRR exercise spot must be finite."), input.Strike);
    }

    private static double Intrinsic(OptionRight right, double spot, double strike) =>
        right switch
        {
            OptionRight.Call => Math.Max(spot - strike, 0.0),
            OptionRight.Put => Math.Max(strike - spot, 0.0),
            _ => throw new ArgumentOutOfRangeException(nameof(right), right, "Unsupported option right.")
        };

    private static void Validate(BinomialTreeInput input)
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
