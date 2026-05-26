using Helium.Finance.Curves;

namespace Helium.Finance.Volatility;

public sealed class BlackVarianceSurface
{
    private readonly double[] _times;
    private readonly double[] _strikes;
    private readonly double[,] _variances;

    public BlackVarianceSurface(
        IReadOnlyList<double> times,
        IReadOnlyList<double> strikes,
        double[,] variances,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled,
        bool requireNondecreasingVariance = true)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(strikes);
        ArgumentNullException.ThrowIfNull(variances);

        if (times.Count == 0 || strikes.Count == 0)
            throw new ArgumentException("Surface axes must not be empty.");

        if (variances.GetLength(0) != times.Count || variances.GetLength(1) != strikes.Count)
            throw new ArgumentException("Surface value dimensions must match axes.");

        ValidateExtrapolationPolicy(extrapolationPolicy);

        _strikes = strikes.ToArray();
        ValidateAxis(_strikes, nameof(strikes), allowZero: false);

        var sourceTimes = times.ToArray();
        ValidateAxis(sourceTimes, nameof(times), allowZero: true);
        var sourceVariances = (double[,])variances.Clone();

        if (sourceTimes[0] == 0.0)
        {
            _times = sourceTimes;
            _variances = sourceVariances;
        }
        else
        {
            _times = [0.0, .. sourceTimes];
            _variances = new double[_times.Length, _strikes.Length];
            for (var i = 0; i < sourceTimes.Length; i++)
            {
                for (var j = 0; j < _strikes.Length; j++)
                    _variances[i + 1, j] = sourceVariances[i, j];
            }
        }

        ValidateVariances(requireNondecreasingVariance);

        ExtrapolationPolicy = extrapolationPolicy;
        RequireNondecreasingVariance = requireNondecreasingVariance;
    }

    public ExtrapolationPolicy ExtrapolationPolicy { get; }

    public bool RequireNondecreasingVariance { get; }

    public double Variance(double time, double strike)
    {
        ValidateInput(time, nameof(time));
        ValidateInput(strike, nameof(strike));

        if (time == 0.0)
            return 0.0;

        var maxTime = _times[^1];
        if (time > maxTime && ExtrapolationPolicy == ExtrapolationPolicy.Flat)
        {
            var extrapolated = maxTime > 0.0 ? InterpolatedVariance(maxTime, strike) * time / maxTime : 0.0;
            return EnsureFiniteNonnegative(extrapolated, nameof(time), "Black variance must be finite and nonnegative.");
        }

        return InterpolatedVariance(time, strike);
    }

    public double Volatility(double time, double strike)
    {
        ValidateInput(time, nameof(time));
        ValidateInput(strike, nameof(strike));

        if (time == 0.0)
        {
            var firstPositiveTime = _times.FirstOrDefault(candidate => candidate > 0.0);
            var zeroTimeVolatility = firstPositiveTime > 0.0
                ? Math.Sqrt(Variance(firstPositiveTime, strike) / firstPositiveTime)
                : 0.0;
            return EnsureFiniteNonnegative(zeroTimeVolatility, nameof(time), "Black volatility must be finite and nonnegative.");
        }

        var volatility = Math.Sqrt(Variance(time, strike) / time);
        return EnsureFiniteNonnegative(volatility, nameof(time), "Black volatility must be finite and nonnegative.");
    }

    public double StandardDeviation(double time, double strike)
    {
        var standardDeviation = Math.Sqrt(Variance(time, strike));
        return EnsureFiniteNonnegative(standardDeviation, nameof(time), "Standard deviation must be finite and nonnegative.");
    }

    public double ForwardVariance(double start, double end, double strike)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-variance interval must satisfy 0 <= start <= end.");

        var forwardVariance = Variance(end, strike) - Variance(start, strike);
        return EnsureFiniteNonnegative(forwardVariance, nameof(end), "Forward variance must be finite and nonnegative.");
    }

    public double ForwardVolatility(double start, double end, double strike)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-volatility interval must satisfy 0 <= start < end.");

        var forwardVolatility = Math.Sqrt(Math.Max(ForwardVariance(start, end, strike), 0.0) / (end - start));
        return EnsureFiniteNonnegative(forwardVolatility, nameof(end), "Forward volatility must be finite and nonnegative.");
    }

    public static BlackVarianceSurface FromVolatilities(
        IReadOnlyList<double> times,
        IReadOnlyList<double> strikes,
        double[,] volatilities,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled,
        bool requireNondecreasingVariance = true)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(strikes);
        ArgumentNullException.ThrowIfNull(volatilities);

        if (volatilities.GetLength(0) != times.Count || volatilities.GetLength(1) != strikes.Count)
            throw new ArgumentException("Surface value dimensions must match axes.");

        var variances = new double[times.Count, strikes.Count];
        for (var i = 0; i < times.Count; i++)
        {
            var time = times[i];
            if (!double.IsFinite(time) || time < 0.0)
                throw new ArgumentOutOfRangeException(nameof(times), "Volatility times must be finite and nonnegative.");

            for (var j = 0; j < strikes.Count; j++)
            {
                var volatility = volatilities[i, j];
                if (!double.IsFinite(volatility) || volatility < 0.0)
                    throw new ArgumentOutOfRangeException(nameof(volatilities), "Black volatilities must be finite and nonnegative.");

                var variance = volatility * volatility * time;
                if (!double.IsFinite(variance) || variance < 0.0)
                    throw new ArgumentOutOfRangeException(nameof(volatilities), "Black variance converted from volatility must be finite and nonnegative.");

                variances[i, j] = variance;
            }
        }

        return new BlackVarianceSurface(times, strikes, variances, extrapolationPolicy, requireNondecreasingVariance);
    }

    private double InterpolatedVariance(double time, double strike)
    {
        var timeIndex = Locate(_times, time, ExtrapolationPolicy);
        var strikeIndex = Locate(_strikes, strike, ExtrapolationPolicy);

        if (timeIndex.Exact && strikeIndex.Exact)
            return _variances[timeIndex.Lower, strikeIndex.Lower];

        var t0 = _times[timeIndex.Lower];
        var t1 = _times[timeIndex.Upper];
        var k0 = _strikes[strikeIndex.Lower];
        var k1 = _strikes[strikeIndex.Upper];
        var wt = t1 == t0 ? 0.0 : (time - t0) / (t1 - t0);
        var wk = k1 == k0 ? 0.0 : (strike - k0) / (k1 - k0);

        var v00 = _variances[timeIndex.Lower, strikeIndex.Lower];
        var v01 = _variances[timeIndex.Lower, strikeIndex.Upper];
        var v10 = _variances[timeIndex.Upper, strikeIndex.Lower];
        var v11 = _variances[timeIndex.Upper, strikeIndex.Upper];
        var lowerStrikeValue = v00 + wk * (v01 - v00);
        var upperStrikeValue = v10 + wk * (v11 - v10);

        var variance = Math.Max(lowerStrikeValue + wt * (upperStrikeValue - lowerStrikeValue), 0.0);
        return EnsureFiniteNonnegative(variance, nameof(time), "Interpolated variance must be finite and nonnegative.");
    }

    private void ValidateVariances(bool requireNondecreasingVariance)
    {
        for (var i = 0; i < _times.Length; i++)
        {
            for (var j = 0; j < _strikes.Length; j++)
            {
                var variance = _variances[i, j];
                if (!double.IsFinite(variance) || variance < 0.0)
                    throw new ArgumentOutOfRangeException(nameof(variance), "Black variances must be finite and nonnegative.");

                if (_times[i] == 0.0 && variance != 0.0)
                    throw new ArgumentOutOfRangeException(nameof(variance), "Black variance at time zero must be zero.");

                if (requireNondecreasingVariance && i > 0 && variance < _variances[i - 1, j])
                    throw new ArgumentOutOfRangeException(nameof(variance), "Black variances must be nondecreasing by maturity for each strike.");
            }
        }
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

    private static double EnsureFiniteNonnegative(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }

    private static void ValidateExtrapolationPolicy(ExtrapolationPolicy policy)
    {
        if (policy is not (ExtrapolationPolicy.Disabled or ExtrapolationPolicy.Flat))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Variance surfaces support disabled or flat extrapolation.");
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
