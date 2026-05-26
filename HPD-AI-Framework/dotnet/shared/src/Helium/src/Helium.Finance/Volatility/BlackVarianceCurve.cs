using Helium.Finance.Curves;

namespace Helium.Finance.Volatility;

public sealed class BlackVarianceCurve
{
    private readonly double[] _times;
    private readonly double[] _variances;

    public BlackVarianceCurve(
        IEnumerable<CurvePoint> variancePoints,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled,
        bool requireNondecreasingVariance = true)
    {
        ArgumentNullException.ThrowIfNull(variancePoints);
        CurvePolicyValidation.ValidateInterpolation(interpolationPolicy);
        CurvePolicyValidation.ValidateExtrapolation(extrapolationPolicy);

        var input = variancePoints.ToArray();
        foreach (var point in input)
        {
            if (!double.IsFinite(point.Time) || point.Time < 0.0)
                throw new ArgumentOutOfRangeException(nameof(variancePoints), "Black variance times must be finite and nonnegative.");
        }

        var ordered = input;
        if (ordered.Length == 0 || ordered[0].Time != 0.0)
            ordered = [new CurvePoint(0.0, 0.0), .. ordered];

        _times = ordered.Select(point => point.Time).ToArray();
        _variances = ordered.Select(point => point.Value).ToArray();

        LinearInterpolation.ValidateStrictlyIncreasing(_times, _variances);
        for (var i = 0; i < _variances.Length; i++)
        {
            var variance = _variances[i];
            if (!double.IsFinite(variance) || variance < 0.0)
                throw new ArgumentOutOfRangeException(nameof(variancePoints), "Black variances must be finite and nonnegative.");

            if (requireNondecreasingVariance && i > 0 && variance < _variances[i - 1])
                throw new ArgumentOutOfRangeException(nameof(variancePoints), "Black variances must be nondecreasing.");
        }

        InterpolationPolicy = interpolationPolicy;
        ExtrapolationPolicy = extrapolationPolicy;
        RequireNondecreasingVariance = requireNondecreasingVariance;
    }

    public InterpolationPolicy InterpolationPolicy { get; }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public bool RequireNondecreasingVariance { get; }

    public double Variance(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        var maxTime = _times[^1];
        if (time > maxTime && ExtrapolationPolicy == ExtrapolationPolicy.Flat)
        {
            var extrapolated = maxTime > 0.0 ? _variances[^1] * time / maxTime : 0.0;
            return EnsureFiniteNonnegative(extrapolated, nameof(time), "Black variance must be finite and nonnegative.");
        }

        var variance = InterpolationPolicy switch
        {
            InterpolationPolicy.Linear => LinearInterpolation.Evaluate(_times, _variances, time, ExtrapolationPolicy),
            _ => throw new ArgumentOutOfRangeException(nameof(InterpolationPolicy), InterpolationPolicy, "Unsupported interpolation policy.")
        };

        return EnsureFiniteNonnegative(Math.Max(variance, 0.0), nameof(time), "Black variance must be finite and nonnegative.");
    }

    public double Volatility(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        if (time == 0.0)
        {
            var zeroTimeVolatility = _times.Length > 1 && _times[1] > 0.0
                ? Math.Sqrt(_variances[1] / _times[1])
                : 0.0;
            return EnsureFiniteNonnegative(zeroTimeVolatility, nameof(time), "Black volatility must be finite and nonnegative.");
        }

        var volatility = Math.Sqrt(Variance(time) / time);
        return EnsureFiniteNonnegative(volatility, nameof(time), "Black volatility must be finite and nonnegative.");
    }

    public double StandardDeviation(double time)
    {
        var standardDeviation = Math.Sqrt(Variance(time));
        return EnsureFiniteNonnegative(standardDeviation, nameof(time), "Standard deviation must be finite and nonnegative.");
    }

    public double ForwardVariance(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-variance interval must satisfy 0 <= start <= end.");

        var forwardVariance = Variance(end) - Variance(start);
        return EnsureFiniteNonnegative(forwardVariance, nameof(end), "Forward variance must be finite and nonnegative.");
    }

    public double ForwardVolatility(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-volatility interval must satisfy 0 <= start < end.");

        var forwardVolatility = Math.Sqrt(Math.Max(ForwardVariance(start, end), 0.0) / (end - start));
        return EnsureFiniteNonnegative(forwardVolatility, nameof(end), "Forward volatility must be finite and nonnegative.");
    }

    public static BlackVarianceCurve FromVolatilities(
        IEnumerable<CurvePoint> volatilityPoints,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled,
        bool requireNondecreasingVariance = true)
    {
        ArgumentNullException.ThrowIfNull(volatilityPoints);

        var variancePoints = volatilityPoints.Select(point =>
        {
            if (!double.IsFinite(point.Time) || point.Time < 0.0)
                throw new ArgumentOutOfRangeException(nameof(volatilityPoints), "Volatility times must be finite and nonnegative.");

            if (!double.IsFinite(point.Value) || point.Value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(volatilityPoints), "Black volatilities must be finite and nonnegative.");

            var variance = point.Value * point.Value * point.Time;
            if (!double.IsFinite(variance) || variance < 0.0)
                throw new ArgumentOutOfRangeException(nameof(volatilityPoints), "Black variance converted from volatility must be finite and nonnegative.");

            return new CurvePoint(point.Time, variance);
        });

        return new BlackVarianceCurve(variancePoints, interpolationPolicy, extrapolationPolicy, requireNondecreasingVariance);
    }

    private static double EnsureFiniteNonnegative(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }
}
