namespace Helium.Validated;

public static class IntervalMath
{
    private const double Eps = 2.220446049250313e-16;

    public static Interval Exp(Interval x)
    {
        var mid = x.Midpoint;
        var value = Math.Exp(mid);
        var halfWidth = x.Width * 0.5;
        var bound = Math.Exp(x.Hi) * (halfWidth + Eps);
        return Interval.Around(value, bound);
    }

    public static Interval Log(Interval x)
    {
        if (x.Lo <= 0.0)
            throw new ArithmeticException("Log is undefined for intervals containing non-positive values.");

        var mid = x.Midpoint;
        var value = Math.Log(mid);
        var halfWidth = x.Width * 0.5;
        var bound = halfWidth / x.Lo + Eps;
        return Interval.Around(value, bound);
    }

    public static Interval Sin(Interval x)
    {
        var value = Math.Sin(x.Midpoint);
        var bound = x.Width * 0.5 + Eps;
        return Interval.Around(value, bound);
    }

    public static Interval Cos(Interval x)
    {
        var value = Math.Cos(x.Midpoint);
        var bound = x.Width * 0.5 + Eps;
        return Interval.Around(value, bound);
    }

    public static Interval Sqrt(Interval x)
    {
        if (x.Lo < 0.0)
            throw new ArithmeticException("Sqrt is undefined for intervals containing negative values.");

        if (x.Lo == 0.0)
            return new Interval(0.0, Math.Sqrt(x.Hi) + Eps);

        var mid = x.Midpoint;
        var value = Math.Sqrt(mid);
        var halfWidth = x.Width * 0.5;
        var bound = halfWidth / (2.0 * Math.Sqrt(x.Lo)) + Eps;
        return Interval.Around(value, bound);
    }
}
