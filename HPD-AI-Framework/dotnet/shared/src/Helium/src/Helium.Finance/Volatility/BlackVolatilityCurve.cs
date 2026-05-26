using Helium.Finance.Curves;

namespace Helium.Finance.Volatility;

public sealed class BlackVolatilityCurve
{
    private readonly double[] _times;
    private readonly double[] _volatilities;

    public BlackVolatilityCurve(
        IEnumerable<CurvePoint> points,
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(points);
        CurvePolicyValidation.ValidateInterpolation(interpolationPolicy);
        CurvePolicyValidation.ValidateExtrapolation(extrapolationPolicy);

        var input = points.ToArray();
        _times = input.Select(point => point.Time).ToArray();
        _volatilities = input.Select(point => point.Value).ToArray();

        LinearInterpolation.ValidateStrictlyIncreasing(_times, _volatilities);
        for (var i = 0; i < _times.Length; i++)
        {
            if (_times[i] < 0.0)
                throw new ArgumentOutOfRangeException(nameof(points), "Volatility times must be nonnegative.");

            var volatility = _volatilities[i];
            if (!double.IsFinite(volatility) || volatility < 0.0)
                throw new ArgumentOutOfRangeException(nameof(points), "Volatilities must be nonnegative.");
        }

        InterpolationPolicy = interpolationPolicy;
        ExtrapolationPolicy = extrapolationPolicy;
    }

    public InterpolationPolicy InterpolationPolicy { get; }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public double Volatility(double time)
    {
        ValidateTime(time);

        var volatility = InterpolationPolicy switch
        {
            InterpolationPolicy.Linear => LinearInterpolation.Evaluate(_times, _volatilities, time, ExtrapolationPolicy),
            _ => throw new ArgumentOutOfRangeException(nameof(InterpolationPolicy), InterpolationPolicy, "Unsupported interpolation policy.")
        };

        return EnsureFiniteNonnegative(volatility, nameof(time), "Interpolated volatility must be finite and nonnegative.");
    }

    public double StandardDeviation(double time)
    {
        ValidateTime(time);

        var standardDeviation = Volatility(time) * Math.Sqrt(time);
        return EnsureFiniteNonnegative(standardDeviation, nameof(time), "Standard deviation must be finite and nonnegative.");
    }

    private static void ValidateTime(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");
    }

    private static double EnsureFiniteNonnegative(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }
}
