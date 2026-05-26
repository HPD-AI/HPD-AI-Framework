namespace Helium.Finance.Curves;

public sealed class ForwardCurve
{
    private readonly double[] _times;
    private readonly double[] _forwards;

    public ForwardCurve(
        IEnumerable<CurvePoint> points,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(points);
        CurvePolicyValidation.ValidateInterpolation(interpolationPolicy);
        CurvePolicyValidation.ValidateExtrapolation(extrapolationPolicy);

        var input = points.ToArray();
        _times = input.Select(point => point.Time).ToArray();
        _forwards = input.Select(point => point.Value).ToArray();

        LinearInterpolation.ValidateStrictlyIncreasing(_times, _forwards);
        ValidateNonnegativeTimes(_times, nameof(points));
        foreach (var forward in _forwards)
        {
            if (!double.IsFinite(forward))
                throw new ArgumentOutOfRangeException(nameof(points), "Forward values must be finite.");
        }

        InterpolationPolicy = interpolationPolicy;
        ExtrapolationPolicy = extrapolationPolicy;
    }

    public InterpolationPolicy InterpolationPolicy { get; }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public double Forward(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        var forward = InterpolationPolicy switch
        {
            InterpolationPolicy.Linear => LinearInterpolation.Evaluate(_times, _forwards, time, ExtrapolationPolicy),
            _ => throw new ArgumentOutOfRangeException(nameof(InterpolationPolicy), InterpolationPolicy, "Unsupported interpolation policy.")
        };

        if (!double.IsFinite(forward))
            throw new ArgumentOutOfRangeException(nameof(time), "Forward value must be finite.");

        return forward;
    }

    public double SimpleForwardReturn(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-return interval must satisfy 0 <= start < end.");

        var startForward = Forward(start);
        if (startForward == 0.0)
            throw new InvalidOperationException("Cannot compute a forward return from a zero starting forward.");

        var forwardReturn = Forward(end) / startForward - 1.0;
        if (!double.IsFinite(forwardReturn))
            throw new ArgumentOutOfRangeException(nameof(end), "Simple forward return must be finite.");

        return forwardReturn;
    }

    public double ContinuouslyCompoundedForwardReturn(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-return interval must satisfy 0 <= start < end.");

        var startForward = Forward(start);
        var endForward = Forward(end);
        if (startForward <= 0.0 || endForward <= 0.0)
            throw new InvalidOperationException("Continuously compounded forward return requires positive forward values.");

        var forwardReturn = Math.Log(endForward / startForward) / (end - start);
        if (!double.IsFinite(forwardReturn))
            throw new ArgumentOutOfRangeException(nameof(end), "Continuously compounded forward return must be finite.");

        return forwardReturn;
    }

    public static double CostOfCarryForward(
        double spot,
        double discountFactor,
        double incomeDiscountFactor = 1.0)
    {
        if (!double.IsFinite(spot))
            throw new ArgumentOutOfRangeException(nameof(spot), "Spot must be finite.");

        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(discountFactor), "Discount factor must be finite and positive.");

        if (!double.IsFinite(incomeDiscountFactor) || incomeDiscountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(incomeDiscountFactor), "Income discount factor must be finite and positive.");

        var forward = spot * incomeDiscountFactor / discountFactor;
        if (!double.IsFinite(forward))
            throw new ArgumentOutOfRangeException(nameof(spot), "Forward value must be finite.");

        return forward;
    }

    public static ForwardCurve FromSpotAndDiscountCurves(
        double spot,
        IReadOnlyList<double> times,
        DiscountCurve fundingCurve,
        DiscountCurve? incomeCurve = null,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(fundingCurve);

        var points = new CurvePoint[times.Count];
        for (var i = 0; i < times.Count; i++)
        {
            var time = times[i];
            if (!double.IsFinite(time) || time < 0.0)
                throw new ArgumentOutOfRangeException(nameof(times), "Forward times must be finite and nonnegative.");

            var incomeDiscount = incomeCurve?.DiscountFactor(time) ?? 1.0;
            points[i] = new CurvePoint(
                time,
                CostOfCarryForward(spot, fundingCurve.DiscountFactor(time), incomeDiscount));
        }

        return new ForwardCurve(points, interpolationPolicy, extrapolationPolicy);
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
