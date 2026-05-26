namespace Helium.Finance.Volatility;

public static class VolatilitySurfaceValidation
{
    public static VolatilitySurfaceValidationResult ValidateBlackSurface(
        IReadOnlyList<double> times,
        IReadOnlyList<double> strikes,
        double[,] values,
        bool requireNonnegativeStrikes = true,
        bool requireNondecreasingTotalVariance = true)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(strikes);
        ArgumentNullException.ThrowIfNull(values);

        var diagnostics = new List<VolatilitySurfaceDiagnostic>();

        if (times.Count == 0)
            diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.EmptyTimeAxis, -1, -1, "Surface time axis must not be empty."));

        if (strikes.Count == 0)
            diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.EmptyStrikeAxis, -1, -1, "Surface strike axis must not be empty."));

        if (values.GetLength(0) != times.Count || values.GetLength(1) != strikes.Count)
        {
            diagnostics.Add(new VolatilitySurfaceDiagnostic(
                VolatilitySurfaceDiagnosticCode.DimensionMismatch,
                -1,
                -1,
                "Surface value dimensions must match time and strike axes."));
        }

        ValidateTimes(times, diagnostics);
        ValidateStrikes(strikes, requireNonnegativeStrikes, diagnostics);
        ValidateValues(times.Count, strikes.Count, values, diagnostics);
        if (requireNondecreasingTotalVariance)
            ValidateTotalVariance(times, strikes.Count, values, diagnostics);

        return new VolatilitySurfaceValidationResult(diagnostics);
    }

    private static void ValidateTimes(
        IReadOnlyList<double> times,
        List<VolatilitySurfaceDiagnostic> diagnostics)
    {
        for (var i = 0; i < times.Count; i++)
        {
            var time = times[i];
            if (!double.IsFinite(time))
            {
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NonFiniteTime, i, -1, "Surface time must be finite."));
                continue;
            }

            if (time < 0.0)
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NegativeTime, i, -1, "Surface time must be nonnegative."));

            if (i > 0 && double.IsFinite(times[i - 1]) && time <= times[i - 1])
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.DuplicateOrUnorderedTime, i, -1, "Surface times must be strictly increasing."));
        }
    }

    private static void ValidateStrikes(
        IReadOnlyList<double> strikes,
        bool requireNonnegativeStrikes,
        List<VolatilitySurfaceDiagnostic> diagnostics)
    {
        for (var i = 0; i < strikes.Count; i++)
        {
            var strike = strikes[i];
            if (!double.IsFinite(strike))
            {
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NonFiniteStrike, -1, i, "Surface strike must be finite."));
                continue;
            }

            if (requireNonnegativeStrikes && strike < 0.0)
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NegativeStrike, -1, i, "Black volatility surface strikes must be positive."));

            if (requireNonnegativeStrikes && strike == 0.0)
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NonPositiveStrike, -1, i, "Black volatility surface strikes must be positive."));

            if (i > 0 && double.IsFinite(strikes[i - 1]) && strike <= strikes[i - 1])
                diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.DuplicateOrUnorderedStrike, -1, i, "Surface strikes must be strictly increasing."));
        }
    }

    private static void ValidateValues(
        int timeCount,
        int strikeCount,
        double[,] values,
        List<VolatilitySurfaceDiagnostic> diagnostics)
    {
        var rows = Math.Min(timeCount, values.GetLength(0));
        var columns = Math.Min(strikeCount, values.GetLength(1));

        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < columns; j++)
            {
                var volatility = values[i, j];
                if (!double.IsFinite(volatility))
                {
                    diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NonFiniteVolatility, i, j, "Surface volatility must be finite."));
                    continue;
                }

                if (volatility < 0.0)
                    diagnostics.Add(new VolatilitySurfaceDiagnostic(VolatilitySurfaceDiagnosticCode.NegativeVolatility, i, j, "Surface volatility must be nonnegative."));
            }
        }
    }

    private static void ValidateTotalVariance(
        IReadOnlyList<double> times,
        int strikeCount,
        double[,] values,
        List<VolatilitySurfaceDiagnostic> diagnostics)
    {
        var rows = Math.Min(times.Count, values.GetLength(0));
        var columns = Math.Min(strikeCount, values.GetLength(1));

        for (var i = 1; i < rows; i++)
        {
            var previousTime = times[i - 1];
            var time = times[i];
            if (!double.IsFinite(previousTime) || !double.IsFinite(time) || previousTime < 0.0 || time < 0.0)
                continue;

            for (var j = 0; j < columns; j++)
            {
                var previousVolatility = values[i - 1, j];
                var volatility = values[i, j];
                if (!double.IsFinite(previousVolatility)
                    || !double.IsFinite(volatility)
                    || previousVolatility < 0.0
                    || volatility < 0.0)
                {
                    continue;
                }

                var previousTotalVariance = previousVolatility * previousVolatility * previousTime;
                var totalVariance = volatility * volatility * time;
                if (!double.IsFinite(previousTotalVariance) || !double.IsFinite(totalVariance))
                    continue;

                if (totalVariance < previousTotalVariance)
                {
                    diagnostics.Add(new VolatilitySurfaceDiagnostic(
                        VolatilitySurfaceDiagnosticCode.DecreasingTotalVariance,
                        i,
                        j,
                        "Black total variance must be nondecreasing by maturity for each strike."));
                }
            }
        }
    }
}
