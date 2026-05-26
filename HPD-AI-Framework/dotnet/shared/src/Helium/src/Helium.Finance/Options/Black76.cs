using Helium.Finance.Distributions;
using Helium.Finance.Solvers;

namespace Helium.Finance.Options;

public static class Black76
{
    public static double Price(Black76Input input)
    {
        Validate(input);

        var eta = input.Right == OptionRight.Call ? 1.0 : -1.0;
        var intrinsic = DiscountedIntrinsic(input.DiscountFactor, eta, input.Forward, input.Strike);

        if (input.TimeToExpiry == 0.0 || input.Volatility == 0.0 || input.Forward == 0.0)
            return intrinsic;

        if (input.Strike == 0.0)
        {
            return input.Right == OptionRight.Call
                ? MultiplyFinite(input.DiscountFactor, input.Forward, "Discounted forward payoff must be finite.")
                : 0.0;
        }

        var stdDev = input.StandardDeviation;
        var d1 = (Math.Log(input.Forward / input.Strike) + 0.5 * stdDev * stdDev) / stdDev;
        var d2 = d1 - stdDev;
        var price = eta * input.DiscountFactor *
            (input.Forward * NormalDistribution.Cdf(eta * d1) - input.Strike * NormalDistribution.Cdf(eta * d2));

        EnsureFiniteNonNegativePrice(price);
        return price;
    }

    public static void BatchPrice(ReadOnlySpan<Black76Input> inputs, Span<double> destination)
    {
        if (destination.Length < inputs.Length)
            throw new ArgumentException("Destination span must be at least as long as the input span.", nameof(destination));

        for (var i = 0; i < inputs.Length; i++)
            destination[i] = Price(inputs[i]);
    }

    public static ImpliedVolatilityResult ImpliedVolatility(
        Black76InputWithoutVolatility input,
        double marketPrice,
        ImpliedVolatilityOptions? options = null)
    {
        if (!IsValid(input))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (!double.IsFinite(marketPrice))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (!(options ?? ImpliedVolatilityOptions.Default).TryNormalize(out var settings))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (!TryPrice(input.WithVolatility(0.0), out var intrinsic))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        var upperBound = input.Right == OptionRight.Call
            ? input.DiscountFactor * input.Forward
            : input.DiscountFactor * input.Strike;
        if (!double.IsFinite(upperBound) || upperBound < 0.0)
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (marketPrice < intrinsic - settings.PriceTolerance)
            return ImpliedFailure(ImpliedVolatilityStatus.BelowIntrinsic);

        if (marketPrice > upperBound + settings.PriceTolerance)
            return ImpliedFailure(ImpliedVolatilityStatus.AboveUpperBound);

        if (Math.Abs(marketPrice - intrinsic) <= settings.PriceTolerance)
        {
            var root = new RootResult(true, 0.0, 0.0, 0, 0, 0.0, 0.0, RootStatus.Converged);
            return new ImpliedVolatilityResult(true, 0.0, 0.0, 0, ImpliedVolatilityStatus.Converged, root);
        }

        var solveInput = input;
        var solveMarketPrice = marketPrice;
        if (TryGetOutOfTheMoneyEquivalent(input, marketPrice, out var outOfTheMoneyInput, out var outOfTheMoneyPrice))
        {
            solveInput = outOfTheMoneyInput;
            solveMarketPrice = outOfTheMoneyPrice;
        }

        var lower = Math.Max(0.0, settings.LowerVolatility);
        var upper = Math.Max(settings.UpperVolatility, lower + 1e-8);

        double Objective(double volatility)
        {
            if (!TryPrice(solveInput.WithVolatility(volatility), out var price))
                return double.NaN;

            var residual = price - solveMarketPrice;
            return double.IsFinite(residual) ? residual : double.NaN;
        }

        double Vega(double volatility)
        {
            if (!TryVega(solveInput.WithVolatility(volatility), out var vega))
                return double.NaN;

            return vega;
        }

        var midpoint = lower + 0.5 * (upper - lower);
        var guess = InitialVolatilityGuess(solveInput, solveMarketPrice, lower, upper, midpoint);
        var rootResult = RootFinders.NewtonSafe(
            Objective,
            Vega,
            lower,
            upper,
            guess,
            settings.PriceTolerance,
            settings.MaxIterations);

        if (!rootResult.Converged)
        {
            rootResult = RootFinders.BrentFromGuess(
                Objective,
                guess,
                step: 0.5 * (upper - lower),
                settings.PriceTolerance,
                settings.MaxIterations,
                settings.MaxBracketExpansions);
        }

        if (!rootResult.Converged)
            return FromRootFailure(rootResult);

        return new ImpliedVolatilityResult(
            true,
            rootResult.Root,
            rootResult.FunctionValue,
            rootResult.Iterations,
            ImpliedVolatilityStatus.Converged,
            rootResult);
    }

    public static OptionGreeks PriceAndGreeks(Black76Input input)
    {
        Validate(input);

        var eta = input.Right == OptionRight.Call ? 1.0 : -1.0;
        var intrinsic = DiscountedIntrinsic(input.DiscountFactor, eta, input.Forward, input.Strike);

        if (input.TimeToExpiry == 0.0 || input.Volatility == 0.0 || input.Forward == 0.0)
        {
            var boundaryDelta = input.Right == OptionRight.Call
                ? BoundaryCallDelta(input.Forward, input.Strike, input.DiscountFactor)
                : BoundaryPutDelta(input.Forward, input.Strike, input.DiscountFactor);

            return new OptionGreeks(intrinsic, boundaryDelta, 0.0, 0.0, 0.0, 0.0);
        }

        if (input.Strike == 0.0)
        {
            return input.Right == OptionRight.Call
                ? new OptionGreeks(MultiplyFinite(input.DiscountFactor, input.Forward, "Discounted forward payoff must be finite."), input.DiscountFactor, 0.0, 0.0, 0.0, 0.0)
                : new OptionGreeks(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        var stdDev = input.StandardDeviation;
        var d1 = (Math.Log(input.Forward / input.Strike) + 0.5 * stdDev * stdDev) / stdDev;
        var d2 = d1 - stdDev;
        var nd1 = NormalDistribution.Cdf(eta * d1);
        var nd2 = NormalDistribution.Cdf(eta * d2);
        var price = eta * input.DiscountFactor * (input.Forward * nd1 - input.Strike * nd2);
        EnsureFiniteNonNegativePrice(price);
        var density = NormalDistribution.Pdf(d1);
        var delta = eta * input.DiscountFactor * nd1;
        var gamma = input.DiscountFactor * density / (input.Forward * stdDev);
        var vega = input.DiscountFactor * input.Forward * density * Math.Sqrt(input.TimeToExpiry);
        var theta = -input.DiscountFactor * input.Forward * density * input.Volatility / (2.0 * Math.Sqrt(input.TimeToExpiry));

        return new OptionGreeks(price, delta, gamma, vega, theta, 0.0);
    }

    private static void Validate(Black76Input input)
    {
        OptionInputValidation.ValidateRight(input.Right);

        if (!double.IsFinite(input.Forward) || input.Forward < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Forward must be finite and nonnegative.");

        if (!double.IsFinite(input.Strike) || input.Strike < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Strike must be finite and nonnegative.");

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Time to expiry must be finite and nonnegative.");

        if (!double.IsFinite(input.Volatility) || input.Volatility < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Volatility must be finite and nonnegative.");

        if (!double.IsFinite(input.DiscountFactor) || input.DiscountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Discount factor must be finite and positive.");

        var standardDeviation = input.StandardDeviation;
        if (!double.IsFinite(standardDeviation))
            throw new ArgumentOutOfRangeException(nameof(input), "Standard deviation must be finite.");
    }

    private static double DiscountedIntrinsic(double discountFactor, double eta, double forward, double strike)
    {
        var intrinsic = Math.Max(eta * (forward - strike), 0.0);
        return MultiplyFinite(discountFactor, intrinsic, "Discounted intrinsic value must be finite.");
    }

    private static double BoundaryCallDelta(double forward, double strike, double discountFactor)
    {
        if (forward > strike)
            return discountFactor;

        return forward == strike ? 0.5 * discountFactor : 0.0;
    }

    private static double BoundaryPutDelta(double forward, double strike, double discountFactor)
    {
        if (forward < strike)
            return -discountFactor;

        return forward == strike ? -0.5 * discountFactor : 0.0;
    }

    private static double MultiplyFinite(double left, double right, string message)
    {
        var result = left * right;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(left), message);

        return result;
    }

    private static bool IsValid(Black76InputWithoutVolatility input)
    {
        return input.Right is OptionRight.Call or OptionRight.Put
            && double.IsFinite(input.Forward)
            && input.Forward >= 0.0
            && double.IsFinite(input.Strike)
            && input.Strike >= 0.0
            && double.IsFinite(input.TimeToExpiry)
            && input.TimeToExpiry >= 0.0
            && double.IsFinite(input.DiscountFactor)
            && input.DiscountFactor > 0.0;
    }

    private static ImpliedVolatilityResult ImpliedFailure(ImpliedVolatilityStatus status)
    {
        var root = new RootResult(false, double.NaN, double.NaN, 0, 0, double.NaN, double.NaN, RootStatus.NonFiniteInput);
        return new ImpliedVolatilityResult(false, double.NaN, double.NaN, 0, status, root);
    }

    private static ImpliedVolatilityResult FromRootFailure(RootResult root)
    {
        var status = root.Status switch
        {
            RootStatus.NoBracket => ImpliedVolatilityStatus.NoBracket,
            RootStatus.MaxIterations => ImpliedVolatilityStatus.MaxIterations,
            RootStatus.NonFiniteFunctionValue => ImpliedVolatilityStatus.NonFiniteFunctionValue,
            RootStatus.FlatDerivative => ImpliedVolatilityStatus.FlatVega,
            _ => ImpliedVolatilityStatus.NonFiniteInput
        };

        return new ImpliedVolatilityResult(false, double.NaN, root.FunctionValue, root.Iterations, status, root);
    }

    private static double InitialVolatilityGuess(
        Black76InputWithoutVolatility input,
        double marketPrice,
        double lower,
        double upper,
        double fallback)
    {
        if (input.TimeToExpiry <= 0.0 || input.DiscountFactor <= 0.0 || input.Forward <= 0.0)
            return fallback;

        var stdDev = InitialStandardDeviationGuess(input, marketPrice);
        var sqrtTime = Math.Sqrt(input.TimeToExpiry);
        var volatility = stdDev / sqrtTime;

        return double.IsFinite(volatility) && volatility >= lower && volatility <= upper
            ? volatility
            : fallback;
    }

    private static double InitialStandardDeviationGuess(
        Black76InputWithoutVolatility input,
        double marketPrice)
    {
        var undiscountedPrice = marketPrice / input.DiscountFactor;
        if (!double.IsFinite(undiscountedPrice) || undiscountedPrice < 0.0)
            return double.NaN;

        if (input.Forward == input.Strike)
            return undiscountedPrice * Math.Sqrt(2.0 * Math.PI) / input.Forward;

        var eta = input.Right == OptionRight.Call ? 1.0 : -1.0;
        var moneynessDelta = eta * (input.Forward - input.Strike);
        var halfMoneynessDelta = 0.5 * moneynessDelta;
        var priceExcess = undiscountedPrice - halfMoneynessDelta;
        var discriminant = priceExcess * priceExcess - (moneynessDelta * moneynessDelta / Math.PI);
        if (!double.IsFinite(discriminant))
            return double.NaN;

        discriminant = Math.Max(discriminant, 0.0);
        var numerator = (priceExcess + Math.Sqrt(discriminant)) * Math.Sqrt(2.0 * Math.PI);
        var denominator = input.Forward + input.Strike;
        var stdDev = numerator / denominator;

        return double.IsFinite(stdDev) && stdDev >= 0.0 ? stdDev : double.NaN;
    }

    private static void EnsureFiniteNonNegativePrice(double price)
    {
        if (!double.IsFinite(price) || price < 0.0)
            throw new ArgumentOutOfRangeException(nameof(price), "Black-76 model price must be finite and nonnegative.");
    }

    private static bool TryGetOutOfTheMoneyEquivalent(
        Black76InputWithoutVolatility input,
        double marketPrice,
        out Black76InputWithoutVolatility equivalentInput,
        out double equivalentMarketPrice)
    {
        equivalentInput = input;
        equivalentMarketPrice = marketPrice;

        var parity = input.DiscountFactor * (input.Forward - input.Strike);
        if (!double.IsFinite(parity))
            return false;

        if (input.Right == OptionRight.Call && input.Forward > input.Strike)
        {
            equivalentInput = new Black76InputWithoutVolatility(
                OptionRight.Put,
                input.Forward,
                input.Strike,
                input.TimeToExpiry,
                input.DiscountFactor);
            equivalentMarketPrice = marketPrice - parity;
            return double.IsFinite(equivalentMarketPrice) && equivalentMarketPrice >= 0.0;
        }

        if (input.Right == OptionRight.Put && input.Strike > input.Forward)
        {
            equivalentInput = new Black76InputWithoutVolatility(
                OptionRight.Call,
                input.Forward,
                input.Strike,
                input.TimeToExpiry,
                input.DiscountFactor);
            equivalentMarketPrice = marketPrice + parity;
            return double.IsFinite(equivalentMarketPrice) && equivalentMarketPrice >= 0.0;
        }

        return false;
    }

    private static bool TryPrice(Black76Input input, out double price)
    {
        try
        {
            price = Price(input);
            return double.IsFinite(price);
        }
        catch (ArgumentOutOfRangeException)
        {
            price = double.NaN;
            return false;
        }
    }

    private static bool TryVega(Black76Input input, out double vega)
    {
        try
        {
            vega = PriceAndGreeks(input).Vega;
            return double.IsFinite(vega);
        }
        catch (ArgumentOutOfRangeException)
        {
            vega = double.NaN;
            return false;
        }
    }
}
