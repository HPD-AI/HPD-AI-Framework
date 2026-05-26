using Helium.Finance.Distributions;

namespace Helium.Finance.Options;

public static class BlackScholes
{
    public static double Price(BlackScholesInput input)
    {
        Validate(input);

        var (forward, discountFactor) = ForwardAndDiscount(input);
        return Black76.Price(new Black76Input(
            input.Right,
            forward,
            input.Strike,
            input.TimeToExpiry,
            input.Volatility,
            discountFactor));
    }

    public static void BatchPrice(ReadOnlySpan<BlackScholesInput> inputs, Span<double> destination)
    {
        if (destination.Length < inputs.Length)
            throw new ArgumentException("Destination span must be at least as long as the input span.", nameof(destination));

        for (var i = 0; i < inputs.Length; i++)
            destination[i] = Price(inputs[i]);
    }

    public static OptionGreeks PriceAndGreeks(BlackScholesInput input)
    {
        Validate(input);

        var (forward, discountFactor) = ForwardAndDiscount(input);
        var black = Black76.PriceAndGreeks(new Black76Input(
            input.Right,
            forward,
            input.Strike,
            input.TimeToExpiry,
            input.Volatility,
            discountFactor));

        var forwardDerivative = Math.Exp((input.RiskFreeRate - input.DividendYield) * input.TimeToExpiry);
        if (!double.IsFinite(forwardDerivative))
            throw new ArgumentOutOfRangeException(nameof(input), "Spot forward derivative must be finite.");

        var delta = black.Delta * forwardDerivative;
        var gamma = black.Gamma * forwardDerivative * forwardDerivative;
        var theta = black.Theta;
        var rho = 0.0;

        if (input.TimeToExpiry > 0.0
            && input.Volatility > 0.0
            && input.Spot > 0.0
            && input.Strike > 0.0)
        {
            var stdDev = input.Volatility * Math.Sqrt(input.TimeToExpiry);
            var d1 = (Math.Log(input.Spot / input.Strike)
                + (input.RiskFreeRate - input.DividendYield + 0.5 * input.Volatility * input.Volatility) * input.TimeToExpiry)
                / stdDev;
            var d2 = d1 - stdDev;
            var spotDiscount = Math.Exp(-input.DividendYield * input.TimeToExpiry);
            var strikeDiscount = discountFactor;
            var density = NormalDistribution.Pdf(d1);

            if (input.Right == OptionRight.Call)
            {
                theta = -input.Spot * spotDiscount * density * input.Volatility / (2.0 * Math.Sqrt(input.TimeToExpiry))
                    - input.RiskFreeRate * input.Strike * strikeDiscount * NormalDistribution.Cdf(d2)
                    + input.DividendYield * input.Spot * spotDiscount * NormalDistribution.Cdf(d1);
                rho = input.Strike * input.TimeToExpiry * strikeDiscount * NormalDistribution.Cdf(d2);
            }
            else
            {
                theta = -input.Spot * spotDiscount * density * input.Volatility / (2.0 * Math.Sqrt(input.TimeToExpiry))
                    + input.RiskFreeRate * input.Strike * strikeDiscount * NormalDistribution.Cdf(-d2)
                    - input.DividendYield * input.Spot * spotDiscount * NormalDistribution.Cdf(-d1);
                rho = -input.Strike * input.TimeToExpiry * strikeDiscount * NormalDistribution.Cdf(-d2);
            }
        }

        return new OptionGreeks(black.Price, delta, gamma, black.Vega, theta, rho);
    }

    public static ImpliedVolatilityResult ImpliedVolatility(
        BlackScholesInputWithoutVolatility input,
        double marketPrice,
        ImpliedVolatilityOptions? options = null)
    {
        Validate(input.WithVolatility(0.0));

        var (forward, discountFactor) = ForwardAndDiscount(input.WithVolatility(0.0));
        var blackInput = new Black76InputWithoutVolatility(
            input.Right,
            forward,
            input.Strike,
            input.TimeToExpiry,
            discountFactor);

        return Black76.ImpliedVolatility(blackInput, marketPrice, options);
    }

    private static void Validate(BlackScholesInput input)
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
    }

    private static (double Forward, double DiscountFactor) ForwardAndDiscount(BlackScholesInput input)
    {
        var forward = input.Spot * Math.Exp((input.RiskFreeRate - input.DividendYield) * input.TimeToExpiry);
        if (!double.IsFinite(forward) || forward < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Spot, rates, and time imply a nonfinite forward.");

        var discountFactor = Math.Exp(-input.RiskFreeRate * input.TimeToExpiry);
        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Risk-free rate and time imply a nonpositive or nonfinite discount factor.");

        return (forward, discountFactor);
    }
}
