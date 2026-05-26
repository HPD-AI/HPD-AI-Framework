namespace Helium.Finance.Curves;

public sealed class ZeroCurve
{
    private readonly double[] _times;
    private readonly double[] _zeroRates;

    public ZeroCurve(
        IEnumerable<CurvePoint> points,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(points);
        CurvePolicyValidation.ValidateInterpolation(interpolationPolicy);
        CurvePolicyValidation.ValidateExtrapolation(extrapolationPolicy);

        var input = points.ToArray();
        _times = input.Select(point => point.Time).ToArray();
        _zeroRates = input.Select(point => point.Value).ToArray();

        LinearInterpolation.ValidateStrictlyIncreasing(_times, _zeroRates);
        ValidateNonnegativeTimes(_times, nameof(points));
        InterpolationPolicy = interpolationPolicy;
        ExtrapolationPolicy = extrapolationPolicy;
    }

    public InterpolationPolicy InterpolationPolicy { get; }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public double ZeroRate(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        var zeroRate = InterpolationPolicy switch
        {
            InterpolationPolicy.Linear => LinearInterpolation.Evaluate(_times, _zeroRates, time, ExtrapolationPolicy),
            _ => throw new ArgumentOutOfRangeException(nameof(InterpolationPolicy), InterpolationPolicy, "Unsupported interpolation policy.")
        };

        if (!double.IsFinite(zeroRate))
            throw new ArgumentOutOfRangeException(nameof(time), "Zero rate must be finite.");

        return zeroRate;
    }

    public double DiscountFactor(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        var discountFactor = Math.Exp(-ZeroRate(time) * time);
        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Discount factor must be finite and positive.");

        return discountFactor;
    }

    private static void ValidateNonnegativeTimes(ReadOnlySpan<double> times, string parameterName)
    {
        foreach (var time in times)
        {
            if (time < 0.0)
                throw new ArgumentOutOfRangeException(parameterName, "Curve times must be nonnegative.");
        }
    }
}
