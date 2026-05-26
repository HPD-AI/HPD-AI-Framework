namespace Helium.Finance.Options;

public static class OptionPriceValidation
{
    public static OptionPriceValidationResult ValidateBlack76Price(
        Black76InputWithoutVolatility input,
        double marketPrice,
        double tolerance = 1e-12)
    {
        var diagnostics = new List<OptionPriceDiagnostic>();
        ValidateBlack76Input(input, diagnostics);
        ValidatePrice(marketPrice, "Market price", diagnostics);
        ValidateTolerance(tolerance, diagnostics);

        if (diagnostics.Count > 0)
            return new OptionPriceValidationResult(diagnostics);

        if (!TryGetBounds(() => NoArbitrageBounds.Black76(input), diagnostics, out var bounds))
            return new OptionPriceValidationResult(diagnostics);

        return ValidateAgainstBounds(bounds, marketPrice, tolerance, diagnostics);
    }

    public static OptionPriceValidationResult ValidateBlackScholesPrice(
        BlackScholesInputWithoutVolatility input,
        double marketPrice,
        double tolerance = 1e-12)
    {
        var diagnostics = new List<OptionPriceDiagnostic>();
        ValidateBlackScholesInput(input, diagnostics);
        ValidatePrice(marketPrice, "Market price", diagnostics);
        ValidateTolerance(tolerance, diagnostics);

        if (diagnostics.Count > 0)
            return new OptionPriceValidationResult(diagnostics);

        if (!TryGetBounds(() => NoArbitrageBounds.BlackScholes(input), diagnostics, out var bounds))
            return new OptionPriceValidationResult(diagnostics);

        return ValidateAgainstBounds(bounds, marketPrice, tolerance, diagnostics);
    }

    public static OptionPriceValidationResult ValidateBachelierPrice(
        BachelierInputWithoutVolatility input,
        double marketPrice,
        double tolerance = 1e-12)
    {
        var diagnostics = new List<OptionPriceDiagnostic>();
        ValidateBachelierInput(input, diagnostics);
        ValidatePrice(marketPrice, "Market price", diagnostics);
        ValidateTolerance(tolerance, diagnostics);

        if (diagnostics.Count > 0)
            return new OptionPriceValidationResult(diagnostics);

        if (!TryGetBounds(() => NoArbitrageBounds.Bachelier(input), diagnostics, out var bounds))
            return new OptionPriceValidationResult(diagnostics);

        return ValidateAgainstBounds(bounds, marketPrice, tolerance, diagnostics);
    }

    public static OptionPriceValidationResult ValidatePutCallParity(
        double forward,
        double strike,
        double discountFactor,
        double callPrice,
        double putPrice,
        double tolerance = 1e-10)
    {
        var diagnostics = new List<OptionPriceDiagnostic>();
        ValidatePrice(callPrice, "Call price", diagnostics);
        ValidatePrice(putPrice, "Put price", diagnostics);
        ValidateTolerance(tolerance, diagnostics);

        if (!double.IsFinite(forward))
            diagnostics.Add(new OptionPriceDiagnostic(OptionPriceDiagnosticCode.NonFinitePrice, "Forward must be finite."));

        if (!double.IsFinite(strike))
            diagnostics.Add(new OptionPriceDiagnostic(OptionPriceDiagnosticCode.NonFinitePrice, "Strike must be finite."));

        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            diagnostics.Add(new OptionPriceDiagnostic(OptionPriceDiagnosticCode.NonFinitePrice, "Discount factor must be finite and positive."));

        if (diagnostics.Count > 0)
            return new OptionPriceValidationResult(diagnostics);

        var parity = discountFactor * (forward - strike);
        if (!double.IsFinite(parity))
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.NonFiniteBound,
                "Put-call parity value must be finite."));
            return new OptionPriceValidationResult(diagnostics);
        }

        var residual = callPrice - putPrice - parity;
        if (!double.IsFinite(residual))
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.NonFinitePrice,
                "Put-call parity residual must be finite."));
            return new OptionPriceValidationResult(diagnostics);
        }

        if (Math.Abs(residual) > tolerance)
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.PutCallParityViolation,
                $"Put-call parity residual {residual:R} exceeds tolerance {tolerance:R}."));
        }

        return new OptionPriceValidationResult(diagnostics);
    }

    public static OptionPriceValidationResult ValidateBlackScholesPutCallParity(
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        double callPrice,
        double putPrice,
        double tolerance = 1e-10)
    {
        var diagnostics = new List<OptionPriceDiagnostic>();
        ValidatePrice(callPrice, "Call price", diagnostics);
        ValidatePrice(putPrice, "Put price", diagnostics);
        ValidateTolerance(tolerance, diagnostics);

        if (!double.IsFinite(spot) || spot < 0.0)
            diagnostics.Add(InvalidInput("Black-Scholes spot must be finite and nonnegative."));

        if (!double.IsFinite(strike) || strike < 0.0)
            diagnostics.Add(InvalidInput("Black-Scholes strike must be finite and nonnegative."));

        if (!double.IsFinite(timeToExpiry) || timeToExpiry < 0.0)
            diagnostics.Add(InvalidInput("Black-Scholes time to expiry must be finite and nonnegative."));

        if (!double.IsFinite(riskFreeRate))
            diagnostics.Add(InvalidInput("Black-Scholes risk-free rate must be finite."));

        if (!double.IsFinite(dividendYield))
            diagnostics.Add(InvalidInput("Black-Scholes dividend yield must be finite."));

        if (diagnostics.Count > 0)
            return new OptionPriceValidationResult(diagnostics);

        var discountedSpot = spot * Math.Exp(-dividendYield * timeToExpiry);
        var discountedStrike = strike * Math.Exp(-riskFreeRate * timeToExpiry);
        if (!double.IsFinite(discountedSpot) || !double.IsFinite(discountedStrike))
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.NonFiniteBound,
                "Black-Scholes put-call parity projection must be finite."));
            return new OptionPriceValidationResult(diagnostics);
        }

        var residual = callPrice - putPrice - (discountedSpot - discountedStrike);
        if (!double.IsFinite(residual))
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.NonFinitePrice,
                "Black-Scholes put-call parity residual must be finite."));
            return new OptionPriceValidationResult(diagnostics);
        }

        if (Math.Abs(residual) > tolerance)
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.PutCallParityViolation,
                $"Black-Scholes put-call parity residual {residual:R} exceeds tolerance {tolerance:R}."));
        }

        return new OptionPriceValidationResult(diagnostics);
    }

    private static bool TryGetBounds(
        Func<NoArbitrageBounds> factory,
        List<OptionPriceDiagnostic> diagnostics,
        out NoArbitrageBounds bounds)
    {
        try
        {
            bounds = factory();
            return true;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.NonFiniteBound,
                exception.Message));
            bounds = default;
            return false;
        }
    }

    private static OptionPriceValidationResult ValidateAgainstBounds(
        NoArbitrageBounds bounds,
        double marketPrice,
        double tolerance,
        List<OptionPriceDiagnostic> diagnostics)
    {
        if (marketPrice < bounds.LowerPrice.Lo - tolerance)
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.BelowLowerBound,
                $"Market price {marketPrice:R} is below lower no-arbitrage bound {bounds.LowerPrice.Lo:R}."));
        }

        if (marketPrice > bounds.UpperPrice.Hi + tolerance)
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.AboveUpperBound,
                $"Market price {marketPrice:R} is above upper no-arbitrage bound {bounds.UpperPrice.Hi:R}."));
        }

        return new OptionPriceValidationResult(diagnostics);
    }

    private static void ValidateBlack76Input(
        Black76InputWithoutVolatility input,
        List<OptionPriceDiagnostic> diagnostics)
    {
        ValidateRight(input.Right, diagnostics);

        if (!double.IsFinite(input.Forward) || input.Forward < 0.0)
            diagnostics.Add(InvalidInput("Black-76 forward must be finite and nonnegative."));

        if (!double.IsFinite(input.Strike) || input.Strike < 0.0)
            diagnostics.Add(InvalidInput("Black-76 strike must be finite and nonnegative."));

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            diagnostics.Add(InvalidInput("Black-76 time to expiry must be finite and nonnegative."));

        if (!double.IsFinite(input.DiscountFactor) || input.DiscountFactor <= 0.0)
            diagnostics.Add(InvalidInput("Black-76 discount factor must be finite and positive."));
    }

    private static void ValidateBlackScholesInput(
        BlackScholesInputWithoutVolatility input,
        List<OptionPriceDiagnostic> diagnostics)
    {
        ValidateRight(input.Right, diagnostics);

        if (!double.IsFinite(input.Spot) || input.Spot < 0.0)
            diagnostics.Add(InvalidInput("Black-Scholes spot must be finite and nonnegative."));

        if (!double.IsFinite(input.Strike) || input.Strike < 0.0)
            diagnostics.Add(InvalidInput("Black-Scholes strike must be finite and nonnegative."));

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            diagnostics.Add(InvalidInput("Black-Scholes time to expiry must be finite and nonnegative."));

        if (!double.IsFinite(input.RiskFreeRate))
            diagnostics.Add(InvalidInput("Black-Scholes risk-free rate must be finite."));

        if (!double.IsFinite(input.DividendYield))
            diagnostics.Add(InvalidInput("Black-Scholes dividend yield must be finite."));
    }

    private static void ValidateBachelierInput(
        BachelierInputWithoutVolatility input,
        List<OptionPriceDiagnostic> diagnostics)
    {
        ValidateRight(input.Right, diagnostics);

        if (!double.IsFinite(input.Forward))
            diagnostics.Add(InvalidInput("Bachelier forward must be finite."));

        if (!double.IsFinite(input.Strike))
            diagnostics.Add(InvalidInput("Bachelier strike must be finite."));

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            diagnostics.Add(InvalidInput("Bachelier time to expiry must be finite and nonnegative."));

        if (!double.IsFinite(input.DiscountFactor) || input.DiscountFactor <= 0.0)
            diagnostics.Add(InvalidInput("Bachelier discount factor must be finite and positive."));
    }

    private static OptionPriceDiagnostic InvalidInput(string message) =>
        new(OptionPriceDiagnosticCode.InvalidInput, message);

    private static void ValidateRight(OptionRight right, List<OptionPriceDiagnostic> diagnostics)
    {
        if (right is not (OptionRight.Call or OptionRight.Put))
            diagnostics.Add(InvalidInput("Option right must be Call or Put."));
    }

    private static void ValidatePrice(
        double price,
        string name,
        List<OptionPriceDiagnostic> diagnostics)
    {
        if (!double.IsFinite(price))
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.NonFinitePrice,
                $"{name} must be finite."));
        }
    }

    private static void ValidateTolerance(
        double tolerance,
        List<OptionPriceDiagnostic> diagnostics)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0.0)
        {
            diagnostics.Add(new OptionPriceDiagnostic(
                OptionPriceDiagnosticCode.InvalidTolerance,
                "Tolerance must be finite and nonnegative."));
        }
    }
}
