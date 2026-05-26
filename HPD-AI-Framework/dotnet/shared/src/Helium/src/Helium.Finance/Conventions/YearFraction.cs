using System.Numerics;
using Helium.Primitives;

namespace Helium.Finance.Conventions;

public readonly record struct YearFraction
{
    public YearFraction(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Year fraction must be finite.");

        Value = value;
    }

    public double Value { get; }

    public static YearFraction FromRational(Rational value)
    {
        return new YearFraction(FinanceConvert.ToDouble(value));
    }
}

public static class FinanceConvert
{
    public static double ToDouble(Rational value)
    {
        var numerator = (BigInteger)value.Numerator;
        var denominator = (BigInteger)value.Denominator;
        var result = (double)numerator / (double)denominator;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(value), "Finance rational conversion must produce a finite double.");

        return result;
    }
}
