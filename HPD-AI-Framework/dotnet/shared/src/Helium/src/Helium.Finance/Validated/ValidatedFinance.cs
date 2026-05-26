using Helium.Validated;

namespace Helium.Finance.Validated;

public static class ValidatedFinance
{
    public static Interval ToInterval(double value, double absoluteRadius)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be finite.");

        if (!double.IsFinite(absoluteRadius) || absoluteRadius < 0.0)
            throw new ArgumentOutOfRangeException(nameof(absoluteRadius), "Radius must be finite and nonnegative.");

        return new Interval(value - absoluteRadius, value + absoluteRadius);
    }

    public static Interval DiscountFactor(Interval continuouslyCompoundedRate, Interval time)
    {
        if (time.Lo < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time interval must be nonnegative.");

        return IntervalMath.Exp(-(continuouslyCompoundedRate * time));
    }

    public static bool ProvesNonNegative(Interval value) => value.Lo >= 0.0;

    public static bool ProvesAtLeast(Interval value, Interval lowerBound) => (value - lowerBound).Lo >= 0.0;

    public static bool ProvesAtMost(Interval value, Interval upperBound) => (upperBound - value).Lo >= 0.0;

    public static bool ContainsPutCallParity(
        Interval callPrice,
        Interval putPrice,
        Interval discountFactor,
        Interval forward,
        Interval strike)
    {
        var parityResidual = callPrice - putPrice - discountFactor * (forward - strike);
        return parityResidual.ContainsZero;
    }
}
