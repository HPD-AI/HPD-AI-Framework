using Helium.Validated;

namespace Helium.Finance.Options;

public readonly record struct NoArbitrageBounds
{
    public NoArbitrageBounds(Interval LowerPrice, Interval UpperPrice)
    {
        if (LowerPrice.Lo < 0.0)
            throw new ArgumentOutOfRangeException(nameof(LowerPrice), "No-arbitrage lower price bound must be nonnegative.");

        if (LowerPrice.Lo > UpperPrice.Hi)
            throw new ArgumentOutOfRangeException(nameof(UpperPrice), "No-arbitrage lower bound cannot exceed upper bound.");

        this.LowerPrice = LowerPrice;
        this.UpperPrice = UpperPrice;
    }

    public Interval LowerPrice { get; }

    public Interval UpperPrice { get; }

    public bool Contains(double price) =>
        double.IsFinite(price) && LowerPrice.Lo <= price && price <= UpperPrice.Hi;

    public static NoArbitrageBounds Black76(Black76InputWithoutVolatility input)
    {
        OptionInputValidation.ValidateRight(input.Right);

        if (!double.IsFinite(input.Forward) || input.Forward < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-76 bounds require finite nonnegative forward.");

        if (!double.IsFinite(input.Strike) || input.Strike < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-76 bounds require finite nonnegative strike.");

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-76 bounds require finite nonnegative time to expiry.");

        if (!double.IsFinite(input.DiscountFactor) || input.DiscountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-76 bounds require finite positive discount factor.");

        var intrinsic = DiscountedIntrinsic(
            input.DiscountFactor,
            input.Right == OptionRight.Call ? input.Forward - input.Strike : input.Strike - input.Forward);

        var upper = input.Right == OptionRight.Call
            ? DiscountedPayoff(input.DiscountFactor, input.Forward)
            : DiscountedPayoff(input.DiscountFactor, input.Strike);

        return new NoArbitrageBounds(Interval.Point(intrinsic), Interval.Point(upper));
    }

    public static NoArbitrageBounds BlackScholes(BlackScholesInputWithoutVolatility input)
    {
        OptionInputValidation.ValidateRight(input.Right);

        if (!double.IsFinite(input.Spot) || input.Spot < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-Scholes bounds require finite nonnegative spot.");

        if (!double.IsFinite(input.Strike) || input.Strike < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-Scholes bounds require finite nonnegative strike.");

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Black-Scholes bounds require finite nonnegative time to expiry.");

        if (!double.IsFinite(input.RiskFreeRate))
            throw new ArgumentOutOfRangeException(nameof(input), "Black-Scholes bounds require finite risk-free rate.");

        if (!double.IsFinite(input.DividendYield))
            throw new ArgumentOutOfRangeException(nameof(input), "Black-Scholes bounds require finite dividend yield.");

        var discountedSpot = DiscountedValue(input.Spot, input.DividendYield, input.TimeToExpiry, nameof(input), "Discounted spot bound must be finite and nonnegative.");
        var discountedStrike = DiscountedValue(input.Strike, input.RiskFreeRate, input.TimeToExpiry, nameof(input), "Discounted strike bound must be finite and nonnegative.");
        var intrinsic = input.Right == OptionRight.Call
            ? Math.Max(discountedSpot - discountedStrike, 0.0)
            : Math.Max(discountedStrike - discountedSpot, 0.0);
        var upper = input.Right == OptionRight.Call
            ? discountedSpot
            : discountedStrike;

        return new NoArbitrageBounds(Interval.Point(intrinsic), Interval.Point(upper));
    }

    public static NoArbitrageBounds Bachelier(BachelierInputWithoutVolatility input)
    {
        OptionInputValidation.ValidateRight(input.Right);

        if (!double.IsFinite(input.Forward))
            throw new ArgumentOutOfRangeException(nameof(input), "Bachelier bounds require finite forward.");

        if (!double.IsFinite(input.Strike))
            throw new ArgumentOutOfRangeException(nameof(input), "Bachelier bounds require finite strike.");

        if (!double.IsFinite(input.TimeToExpiry) || input.TimeToExpiry < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Bachelier bounds require finite nonnegative time to expiry.");

        if (!double.IsFinite(input.DiscountFactor) || input.DiscountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Bachelier bounds require finite positive discount factor.");

        var intrinsic = DiscountedIntrinsic(
            input.DiscountFactor,
            input.Right == OptionRight.Call ? input.Forward - input.Strike : input.Strike - input.Forward);

        return new NoArbitrageBounds(Interval.Point(intrinsic), new Interval(intrinsic, double.PositiveInfinity));
    }

    public void Deconstruct(out Interval LowerPrice, out Interval UpperPrice)
    {
        LowerPrice = this.LowerPrice;
        UpperPrice = this.UpperPrice;
    }

    private static double DiscountedIntrinsic(double discountFactor, double undiscountedIntrinsic)
    {
        var intrinsic = Math.Max(undiscountedIntrinsic, 0.0);
        return DiscountedPayoff(discountFactor, intrinsic);
    }

    private static double DiscountedPayoff(double discountFactor, double payoff)
    {
        var value = discountFactor * payoff;
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(nameof(payoff), "No-arbitrage bound must be finite and nonnegative.");

        return value;
    }

    private static double DiscountedValue(double amount, double rate, double time, string parameterName, string message)
    {
        var discountFactor = Math.Exp(-rate * time);
        var value = amount * discountFactor;
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }
}
