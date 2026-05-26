namespace Helium.Finance.Curves;

internal static class LinearInterpolation
{
    public static double Evaluate(
        ReadOnlySpan<double> xs,
        ReadOnlySpan<double> ys,
        double x,
        ExtrapolationPolicy extrapolationPolicy)
    {
        if (!double.IsFinite(x))
            throw new ArgumentOutOfRangeException(nameof(x), "Input must be finite.");

        if (xs.Length != ys.Length || xs.Length == 0)
            throw new ArgumentException("Curve inputs must have the same nonzero length.");

        if (xs.Length == 1)
        {
            if (x == xs[0] || extrapolationPolicy == ExtrapolationPolicy.Flat)
                return ys[0];

            throw new ArgumentOutOfRangeException(nameof(x), "Cannot interpolate a one-point curve without flat extrapolation.");
        }

        if (x < xs[0])
            return Extrapolate(xs[0], ys[0], xs[1], ys[1], x, extrapolationPolicy, nameof(x));

        var last = xs.Length - 1;
        if (x > xs[last])
            return Extrapolate(xs[last - 1], ys[last - 1], xs[last], ys[last], x, extrapolationPolicy, nameof(x));

        var index = BinarySearch(xs, x);
        if (index >= 0)
            return ys[index];

        var upper = ~index;
        var lower = upper - 1;
        return Interpolate(xs[lower], ys[lower], xs[upper], ys[upper], x);
    }

    private static int BinarySearch(ReadOnlySpan<double> values, double value)
    {
        var lower = 0;
        var upper = values.Length - 1;

        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (values[middle] == value)
                return middle;

            if (values[middle] < value)
                lower = middle + 1;
            else
                upper = middle - 1;
        }

        return ~lower;
    }

    public static void ValidateStrictlyIncreasing(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys)
    {
        if (xs.Length != ys.Length)
            throw new ArgumentException("Curve inputs must have the same length.");

        if (xs.Length == 0)
            throw new ArgumentException("Curve inputs must not be empty.");

        for (var i = 0; i < xs.Length; i++)
        {
            if (!double.IsFinite(xs[i]))
                throw new ArgumentOutOfRangeException(nameof(xs), "Times must be finite.");

            if (!double.IsFinite(ys[i]))
                throw new ArgumentOutOfRangeException(nameof(ys), "Values must be finite.");

            if (i > 0 && xs[i] <= xs[i - 1])
                throw new ArgumentException("Times must be strictly increasing.", nameof(xs));
        }
    }

    private static double Extrapolate(
        double x0,
        double y0,
        double x1,
        double y1,
        double x,
        ExtrapolationPolicy policy,
        string parameterName)
    {
        return policy switch
        {
            ExtrapolationPolicy.Flat => x < x0 ? y0 : y1,
            ExtrapolationPolicy.Linear => Interpolate(x0, y0, x1, y1, x),
            _ => throw new ArgumentOutOfRangeException(parameterName, x, "Extrapolation is disabled.")
        };
    }

    private static double Interpolate(double x0, double y0, double x1, double y1, double x)
    {
        var weight = (x - x0) / (x1 - x0);
        return y0 + weight * (y1 - y0);
    }
}
