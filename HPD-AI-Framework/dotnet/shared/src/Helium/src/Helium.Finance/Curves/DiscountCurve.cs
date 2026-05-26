namespace Helium.Finance.Curves;

public sealed class DiscountCurve
{
    private readonly double[] _times;
    private readonly double[] _discountFactors;

    public DiscountCurve(
        IEnumerable<CurvePoint> points,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(points);
        CurvePolicyValidation.ValidateInterpolation(interpolationPolicy);
        CurvePolicyValidation.ValidateExtrapolation(extrapolationPolicy);

        var input = points.ToArray();
        _times = input.Select(point => point.Time).ToArray();
        _discountFactors = input.Select(point => point.Value).ToArray();

        LinearInterpolation.ValidateStrictlyIncreasing(_times, _discountFactors);
        ValidateNonnegativeTimes(_times, nameof(points));
        foreach (var discountFactor in _discountFactors)
        {
            if (discountFactor <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(points), "Discount factors must be positive.");
        }

        InterpolationPolicy = interpolationPolicy;
        ExtrapolationPolicy = extrapolationPolicy;
    }

    public InterpolationPolicy InterpolationPolicy { get; }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public double DiscountFactor(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        var discountFactor = InterpolationPolicy switch
        {
            InterpolationPolicy.Linear => LinearInterpolation.Evaluate(_times, _discountFactors, time, ExtrapolationPolicy),
            _ => throw new ArgumentOutOfRangeException(nameof(InterpolationPolicy), InterpolationPolicy, "Unsupported interpolation policy.")
        };

        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Interpolated discount factor must be finite and positive.");

        return discountFactor;
    }

    public double ZeroRate(double time)
    {
        if (!double.IsFinite(time) || time <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be positive for zero-rate extraction.");

        var zeroRate = -Math.Log(DiscountFactor(time)) / time;
        if (!double.IsFinite(zeroRate))
            throw new ArgumentOutOfRangeException(nameof(time), "Zero rate must be finite.");

        return zeroRate;
    }

    public double ForwardRate(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-rate interval must satisfy 0 <= start < end.");

        var forwardRate = Math.Log(DiscountFactor(start) / DiscountFactor(end)) / (end - start);
        if (!double.IsFinite(forwardRate))
            throw new ArgumentOutOfRangeException(nameof(end), "Forward rate must be finite.");

        return forwardRate;
    }

    public double ForwardDiscountFactor(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-discount interval must satisfy 0 <= start <= end.");

        var forwardDiscountFactor = DiscountFactor(end) / DiscountFactor(start);
        if (!double.IsFinite(forwardDiscountFactor) || forwardDiscountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward discount factor must be finite and positive.");

        return forwardDiscountFactor;
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
