using Helium.Finance.Curves;

namespace Helium.Finance.Volatility;

public sealed class BlackVolatilitySurface
{
    private readonly double[] _times;
    private readonly double[] _strikes;
    private readonly double[,] _values;

    public BlackVolatilitySurface(
        IReadOnlyList<double> times,
        IReadOnlyList<double> strikes,
        double[,] values,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(strikes);
        ArgumentNullException.ThrowIfNull(values);

        if (times.Count == 0 || strikes.Count == 0)
            throw new ArgumentException("Surface axes must not be empty.");

        if (values.GetLength(0) != times.Count || values.GetLength(1) != strikes.Count)
            throw new ArgumentException("Surface value dimensions must match axes.");

        ValidateExtrapolationPolicy(extrapolationPolicy);

        _times = times.ToArray();
        _strikes = strikes.ToArray();
        _values = (double[,])values.Clone();

        ValidateAxis(_times, nameof(times), allowZero: true);
        ValidateAxis(_strikes, nameof(strikes), allowZero: false);
        for (var i = 0; i < _times.Length; i++)
        {
            for (var j = 0; j < _strikes.Length; j++)
            {
                if (!double.IsFinite(_values[i, j]) || _values[i, j] < 0.0)
                    throw new ArgumentOutOfRangeException(nameof(values), "Surface volatilities must be finite and nonnegative.");
            }
        }

        ExtrapolationPolicy = extrapolationPolicy;
    }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public double Volatility(double time, double strike)
    {
        ValidateInput(time, nameof(time));
        ValidateInput(strike, nameof(strike));

        var timeIndex = Locate(_times, time, ExtrapolationPolicy);
        var strikeIndex = Locate(_strikes, strike, ExtrapolationPolicy);

        if (timeIndex.Exact && strikeIndex.Exact)
            return _values[timeIndex.Lower, strikeIndex.Lower];

        var t0 = _times[timeIndex.Lower];
        var t1 = _times[timeIndex.Upper];
        var k0 = _strikes[strikeIndex.Lower];
        var k1 = _strikes[strikeIndex.Upper];
        var wt = t1 == t0 ? 0.0 : (time - t0) / (t1 - t0);
        var wk = k1 == k0 ? 0.0 : (strike - k0) / (k1 - k0);

        var v00 = _values[timeIndex.Lower, strikeIndex.Lower];
        var v01 = _values[timeIndex.Lower, strikeIndex.Upper];
        var v10 = _values[timeIndex.Upper, strikeIndex.Lower];
        var v11 = _values[timeIndex.Upper, strikeIndex.Upper];
        var lowerStrikeValue = v00 + wk * (v01 - v00);
        var upperStrikeValue = v10 + wk * (v11 - v10);

        var volatility = lowerStrikeValue + wt * (upperStrikeValue - lowerStrikeValue);
        if (!double.IsFinite(volatility) || volatility < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Interpolated volatility must be finite and nonnegative.");

        return volatility;
    }

    public double StandardDeviation(double time, double strike)
    {
        var standardDeviation = Volatility(time, strike) * Math.Sqrt(time);
        if (!double.IsFinite(standardDeviation))
            throw new ArgumentOutOfRangeException(nameof(time), "Standard deviation must be finite.");

        return standardDeviation;
    }

    private static void ValidateAxis(double[] axis, string parameterName, bool allowZero)
    {
        for (var i = 0; i < axis.Length; i++)
        {
            if (!double.IsFinite(axis[i]) || axis[i] < 0.0 || (!allowZero && axis[i] == 0.0))
                throw new ArgumentOutOfRangeException(parameterName, "Axis values must be finite and positive unless zero is explicitly allowed.");

            if (i > 0 && axis[i] <= axis[i - 1])
                throw new ArgumentException("Axis values must be strictly increasing.", parameterName);
        }
    }

    private static void ValidateInput(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, "Surface input must be finite and nonnegative.");
    }

    private static void ValidateExtrapolationPolicy(ExtrapolationPolicy policy)
    {
        if (policy is not (ExtrapolationPolicy.Disabled or ExtrapolationPolicy.Flat))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Volatility surfaces support disabled or flat extrapolation.");
    }

    private static Location Locate(double[] axis, double value, ExtrapolationPolicy extrapolationPolicy)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Surface input must be finite.");

        if (axis.Length == 1)
            return new Location(0, 0, true);

        if (value < axis[0])
        {
            if (extrapolationPolicy != ExtrapolationPolicy.Flat)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Extrapolation is disabled.");

            return new Location(0, 0, true);
        }

        var last = axis.Length - 1;
        if (value > axis[last])
        {
            if (extrapolationPolicy != ExtrapolationPolicy.Flat)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Extrapolation is disabled.");

            return new Location(last, last, true);
        }

        var index = Array.BinarySearch(axis, value);
        if (index >= 0)
            return new Location(index, index, true);

        var upper = ~index;
        return new Location(upper - 1, upper, false);
    }

    private readonly record struct Location(int Lower, int Upper, bool Exact);
}
