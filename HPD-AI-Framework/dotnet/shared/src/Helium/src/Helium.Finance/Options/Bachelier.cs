using Helium.Finance.Distributions;
using Helium.Finance.Solvers;

namespace Helium.Finance.Options;

public static class Bachelier
{
    public static double Price(BachelierInput input)
    {
        Validate(input);

        var eta = input.Right == OptionRight.Call ? 1.0 : -1.0;
        var intrinsic = DiscountedIntrinsic(input.DiscountFactor, eta, input.Forward, input.Strike);

        if (input.TimeToExpiry == 0.0 || input.NormalVolatility == 0.0)
            return intrinsic;

        var stdDev = input.StandardDeviation;
        var d = (input.Forward - input.Strike) / stdDev;
        var price = input.DiscountFactor *
            (eta * (input.Forward - input.Strike) * NormalDistribution.Cdf(eta * d) + stdDev * NormalDistribution.Pdf(d));

        EnsureFiniteNonNegativePrice(price);
        return price;
    }

    public static void BatchPrice(ReadOnlySpan<BachelierInput> inputs, Span<double> destination)
    {
        if (destination.Length < inputs.Length)
            throw new ArgumentException("Destination span must be at least as long as the input span.", nameof(destination));

        for (var i = 0; i < inputs.Length; i++)
            destination[i] = Price(inputs[i]);
    }

    public static ImpliedVolatilityResult ImpliedVolatility(
        BachelierInputWithoutVolatility input,
        double marketPrice,
        ImpliedVolatilityOptions? options = null)
    {
        if (!IsValid(input))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (!double.IsFinite(marketPrice))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (!(options ?? ImpliedVolatilityOptions.Default).TryNormalize(out var settings, defaultUpperVolatility: 1000.0))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (!TryPrice(input.WithNormalVolatility(0.0), out var intrinsic))
            return ImpliedFailure(ImpliedVolatilityStatus.NonFiniteInput);

        if (marketPrice < intrinsic - settings.PriceTolerance)
            return ImpliedFailure(ImpliedVolatilityStatus.BelowIntrinsic);

        if (Math.Abs(marketPrice - intrinsic) <= settings.PriceTolerance)
        {
            var root = new RootResult(true, 0.0, 0.0, 0, 0, 0.0, 0.0, RootStatus.Converged);
            return new ImpliedVolatilityResult(true, 0.0, 0.0, 0, ImpliedVolatilityStatus.Converged, root);
        }

        var lower = Math.Max(0.0, settings.LowerVolatility);
        var upper = Math.Max(settings.UpperVolatility, lower + 1e-8);

        double Objective(double volatility)
        {
            if (!TryPrice(input.WithNormalVolatility(volatility), out var price))
                return double.NaN;

            var residual = price - marketPrice;
            return double.IsFinite(residual) ? residual : double.NaN;
        }

        double Vega(double volatility)
        {
            if (!TryVega(input.WithNormalVolatility(volatility), out var vega))
                return double.NaN;

            return vega;
        }

        var midpoint = lower + 0.5 * (upper - lower);
        var guess = InitialNormalVolatilityGuess(input, marketPrice, lower, upper, midpoint);
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

    public static OptionGreeks PriceAndGreeks(BachelierInput input)
    {
        Validate(input);

        var eta = input.Right == OptionRight.Call ? 1.0 : -1.0;
        var intrinsic = DiscountedIntrinsic(input.DiscountFactor, eta, input.Forward, input.Strike);

        if (input.TimeToExpiry == 0.0 || input.NormalVolatility == 0.0)
        {
            var boundaryDelta = input.Right == OptionRight.Call
                ? BoundaryCallDelta(input.Forward, input.Strike, input.DiscountFactor)
                : BoundaryPutDelta(input.Forward, input.Strike, input.DiscountFactor);

            return new OptionGreeks(intrinsic, boundaryDelta, 0.0, 0.0, 0.0, 0.0);
        }

        var stdDev = input.StandardDeviation;
        var d = (input.Forward - input.Strike) / stdDev;
        var etaD = eta * d;
        var price = input.DiscountFactor *
            (eta * (input.Forward - input.Strike) * NormalDistribution.Cdf(etaD) + stdDev * NormalDistribution.Pdf(d));
        EnsureFiniteNonNegativePrice(price);

        var delta = eta * input.DiscountFactor * NormalDistribution.Cdf(etaD);
        var gamma = input.DiscountFactor * NormalDistribution.Pdf(d) / stdDev;
        var vega = input.DiscountFactor * Math.Sqrt(input.TimeToExpiry) * NormalDistribution.Pdf(d);
        var theta = -0.5 * input.DiscountFactor * input.NormalVolatility * NormalDistribution.Pdf(d) / Math.Sqrt(input.TimeToExpiry);

        return new OptionGreeks(price, delta, gamma, vega, theta, 0.0);
    }

    private static void Validate(BachelierInput input)
    {
        OptionInputValidation.ValidateRight(input.Right);

        if (!double.IsFinite(input.Forward))
            throw new ArgumentOutOfRangeException(nameof(input), "Forward must be finite.");

        if (!double.IsFinite(input.Strike))
            throw new ArgumentOutOfRangeException(nameof(input), "Strike must be finite.");

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Time to expiry must be finite and nonnegative.");

        if (!double.IsFinite(input.NormalVolatility) || input.NormalVolatility < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Normal volatility must be finite and nonnegative.");

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

    private static bool IsValid(BachelierInputWithoutVolatility input)
    {
        return input.Right is OptionRight.Call or OptionRight.Put
            && double.IsFinite(input.Forward)
            && double.IsFinite(input.Strike)
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

    private static double InitialNormalVolatilityGuess(
        BachelierInputWithoutVolatility input,
        double marketPrice,
        double lower,
        double upper,
        double fallback)
    {
        if (input.TimeToExpiry <= 0.0 || input.DiscountFactor <= 0.0)
            return fallback;

        var volatility = ExactImpliedNormalVolatilityGuess(input, marketPrice);
        return double.IsFinite(volatility) && volatility >= lower && volatility <= upper
            ? volatility
            : fallback;
    }

    private static double ExactImpliedNormalVolatilityGuess(
        BachelierInputWithoutVolatility input,
        double marketPrice)
    {
        var undiscountedPrice = marketPrice / input.DiscountFactor;
        if (!double.IsFinite(undiscountedPrice) || undiscountedPrice < 0.0)
            return double.NaN;

        var sqrtTime = Math.Sqrt(input.TimeToExpiry);
        if (!double.IsFinite(sqrtTime) || sqrtTime <= 0.0)
            return double.NaN;

        if (NearlyEqual(input.Strike, input.Forward))
            return undiscountedPrice / (sqrtTime * NormalDistribution.Pdf(0.0));

        var eta = input.Right == OptionRight.Call ? 1.0 : -1.0;
        var intrinsic = Math.Max(eta * (input.Forward - input.Strike), 0.0);
        var timeValue = undiscountedPrice - intrinsic;
        if (!double.IsFinite(timeValue) || timeValue < 0.0)
            return double.NaN;

        if (timeValue == 0.0)
            return 0.0;

        var phiTildeStar = -Math.Abs(timeValue / (input.Strike - input.Forward));
        if (!double.IsFinite(phiTildeStar) || phiTildeStar >= 0.0)
            return double.NaN;

        var xStar = InversePhiTilde(phiTildeStar);
        var volatility = Math.Abs((input.Strike - input.Forward) / (xStar * sqrtTime));
        return double.IsFinite(volatility) && volatility >= 0.0 ? volatility : double.NaN;
    }

    private static double InversePhiTilde(double phiTildeStar)
    {
        if (!double.IsFinite(phiTildeStar) || phiTildeStar >= 0.0)
            return double.NaN;

        double Objective(double x)
        {
            var value = PhiTilde(x);
            return double.IsFinite(value) ? value - phiTildeStar : double.NaN;
        }

        var upper = -1e-12;
        var fUpper = Objective(upper);
        for (var expansion = 0; expansion < 80 && double.IsFinite(fUpper) && fUpper > 0.0; expansion++)
        {
            upper *= 0.1;
            fUpper = Objective(upper);
        }

        if (!double.IsFinite(fUpper) || fUpper > 0.0)
            return double.NaN;

        var lower = -1.0;
        var fLower = Objective(lower);
        for (var expansion = 0; expansion < 1024 && double.IsFinite(fLower) && fLower < 0.0; expansion++)
        {
            lower *= 2.0;
            fLower = Objective(lower);
        }

        if (!double.IsFinite(fLower) || fLower < 0.0)
            return double.NaN;

        var root = RootFinders.Brent(Objective, lower, upper, absoluteTolerance: 1e-14, maxIterations: 128);
        return root.Converged && double.IsFinite(root.Root) && root.Root < 0.0 ? root.Root : double.NaN;
    }

    private static double PhiTilde(double x)
    {
        if (x == 0.0)
            return double.NaN;

        var value = NormalDistribution.Cdf(x) + NormalDistribution.Pdf(x) / x;
        return double.IsFinite(value) ? value : double.NaN;
    }

    private static bool NearlyEqual(double left, double right)
    {
        var scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= 1e-14 * scale;
    }

    private static void EnsureFiniteNonNegativePrice(double price)
    {
        if (!double.IsFinite(price) || price < 0.0)
            throw new ArgumentOutOfRangeException(nameof(price), "Bachelier model price must be finite and nonnegative.");
    }

    private static bool TryPrice(BachelierInput input, out double price)
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

    private static bool TryVega(BachelierInput input, out double vega)
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
