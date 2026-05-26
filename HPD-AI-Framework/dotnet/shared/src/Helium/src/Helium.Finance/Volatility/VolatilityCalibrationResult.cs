using Helium.Finance.Curves;

namespace Helium.Finance.Volatility;

public sealed class VolatilityCalibrationResult
{
    public VolatilityCalibrationResult(IReadOnlyList<VolatilityCalibrationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var snapshot = points.ToArray();
        if (snapshot.Length == 0)
            throw new ArgumentException("Calibration results must contain at least one point.", nameof(points));

        foreach (var point in snapshot)
        {
            if (point.ImpliedVolatility.Converged != (point.ImpliedVolatility.Status == Options.ImpliedVolatilityStatus.Converged))
                throw new ArgumentException("Calibration points must contain consistent implied-volatility diagnostics.", nameof(points));
        }

        Points = snapshot;
    }

    public IReadOnlyList<VolatilityCalibrationPoint> Points { get; }

    public bool AllConverged => Points.Count > 0 && Points.All(point => point.Converged);

    public BlackVolatilityCurve ToBlackVolatilityCurve(
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        if (!AllConverged)
            throw new InvalidOperationException("Cannot build a volatility curve unless every calibration point converged.");

        var points = Points
            .OrderBy(point => point.TimeToExpiry)
            .Select(point => new CurvePoint(point.TimeToExpiry, point.Volatility))
            .ToArray();

        for (var i = 1; i < points.Length; i++)
        {
            if (points[i].Time == points[i - 1].Time)
                throw new InvalidOperationException("Cannot build a volatility term curve from duplicate expiries.");
        }

        return new BlackVolatilityCurve(points, interpolationPolicy, extrapolationPolicy);
    }

    public BlackVarianceCurve ToBlackVarianceCurve(
        InterpolationPolicy interpolationPolicy = InterpolationPolicy.Linear,
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled,
        bool requireNondecreasingVariance = true)
    {
        if (!AllConverged)
            throw new InvalidOperationException("Cannot build a variance curve unless every calibration point converged.");

        var points = Points
            .OrderBy(point => point.TimeToExpiry)
            .Select(point =>
            {
                var variance = point.Volatility * point.Volatility * point.TimeToExpiry;
                if (!double.IsFinite(variance) || variance < 0.0)
                    throw new InvalidOperationException("Calibrated total variance must be finite and nonnegative.");

                return new CurvePoint(point.TimeToExpiry, variance);
            })
            .ToArray();

        for (var i = 1; i < points.Length; i++)
        {
            if (points[i].Time == points[i - 1].Time)
                throw new InvalidOperationException("Cannot build a variance term curve from duplicate expiries.");
        }

        return new BlackVarianceCurve(points, interpolationPolicy, extrapolationPolicy, requireNondecreasingVariance);
    }

    public BlackVolatilitySurface ToBlackVolatilitySurface(
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled)
    {
        var (times, strikes, volatilities) = BuildVolatilityGrid();
        return new BlackVolatilitySurface(times, strikes, volatilities, extrapolationPolicy);
    }

    public BlackVarianceSurface ToBlackVarianceSurface(
        ExtrapolationPolicy extrapolationPolicy = ExtrapolationPolicy.Disabled,
        bool requireNondecreasingVariance = true)
    {
        var (times, strikes, volatilities) = BuildVolatilityGrid();
        return BlackVarianceSurface.FromVolatilities(
            times,
            strikes,
            volatilities,
            extrapolationPolicy,
            requireNondecreasingVariance);
    }

    private (double[] Times, double[] Strikes, double[,] Volatilities) BuildVolatilityGrid()
    {
        if (!AllConverged)
            throw new InvalidOperationException("Cannot build a volatility surface unless every calibration point converged.");

        var times = Points.Select(point => point.TimeToExpiry).Distinct().Order().ToArray();
        var strikes = Points.Select(point => point.Strike).Distinct().Order().ToArray();
        var expectedPointCount = checked(times.Length * strikes.Length);
        if (Points.Count != expectedPointCount)
            throw new InvalidOperationException("Cannot build a volatility surface unless calibration points form a complete expiry/strike grid.");

        var volatilities = new double[times.Length, strikes.Length];
        var populated = new bool[times.Length, strikes.Length];
        foreach (var point in Points)
        {
            var timeIndex = Array.BinarySearch(times, point.TimeToExpiry);
            var strikeIndex = Array.BinarySearch(strikes, point.Strike);
            if (timeIndex < 0 || strikeIndex < 0)
                throw new InvalidOperationException("Calibration point does not map onto the surface grid.");

            if (populated[timeIndex, strikeIndex])
                throw new InvalidOperationException("Cannot build a volatility surface from duplicate expiry/strike calibration points.");

            volatilities[timeIndex, strikeIndex] = point.Volatility;
            populated[timeIndex, strikeIndex] = true;
        }

        for (var i = 0; i < times.Length; i++)
        {
            for (var j = 0; j < strikes.Length; j++)
            {
                if (!populated[i, j])
                    throw new InvalidOperationException("Cannot build a volatility surface unless calibration points form a complete expiry/strike grid.");
            }
        }

        return (times, strikes, volatilities);
    }
}
