namespace Helium.Finance.Curves;

public static class CurveValidation
{
    public static CurveValidationResult ValidateDiscountCurvePoints(
        IReadOnlyList<CurvePoint> points,
        bool requireTimeZero = false,
        bool requireTimeZeroDiscountFactorOne = true,
        bool requireNonIncreasingDiscountFactors = true)
    {
        ArgumentNullException.ThrowIfNull(points);

        var diagnostics = new List<CurveDiagnostic>();
        if (points.Count == 0)
        {
            diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.EmptyCurve, -1, "Curve must contain at least one point."));
            return new CurveValidationResult(diagnostics);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!double.IsFinite(point.Time))
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NonFiniteTime, i, "Curve time must be finite."));

            if (!double.IsFinite(point.Value))
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NonFiniteValue, i, "Curve value must be finite."));

            if (double.IsFinite(point.Time) && point.Time < 0.0)
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NegativeTime, i, "Curve time must be nonnegative."));

            if (double.IsFinite(point.Value) && point.Value <= 0.0)
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NonPositiveDiscountFactor, i, "Discount factor must be positive."));

            if (i > 0)
            {
                var previous = points[i - 1];
                if (double.IsFinite(point.Time) && double.IsFinite(previous.Time) && point.Time <= previous.Time)
                    diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.DuplicateOrUnorderedTime, i, "Curve times must be strictly increasing."));

                if (requireNonIncreasingDiscountFactors &&
                    double.IsFinite(point.Value) &&
                    double.IsFinite(previous.Value) &&
                    point.Value > previous.Value)
                {
                    diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.IncreasingDiscountFactor, i, "Discount factors must be nonincreasing for this validation policy."));
                }
            }
        }

        if (requireTimeZero && points.Count > 0 && points[0].Time != 0.0)
            diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.MissingTimeZero, 0, "Curve must start at time zero."));

        if (requireTimeZeroDiscountFactorOne
            && points.Count > 0
            && points[0].Time == 0.0
            && double.IsFinite(points[0].Value)
            && points[0].Value != 1.0)
        {
            diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.InvalidTimeZeroDiscountFactor, 0, "Discount factor at time zero must be one."));
        }

        return new CurveValidationResult(diagnostics);
    }

    public static CurveValidationResult ValidateZeroCurvePoints(
        IReadOnlyList<CurvePoint> points,
        bool requireTimeZero = false)
    {
        ArgumentNullException.ThrowIfNull(points);

        var diagnostics = new List<CurveDiagnostic>();
        if (points.Count == 0)
        {
            diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.EmptyCurve, -1, "Curve must contain at least one point."));
            return new CurveValidationResult(diagnostics);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!double.IsFinite(point.Time))
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NonFiniteTime, i, "Curve time must be finite."));

            if (!double.IsFinite(point.Value))
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NonFiniteValue, i, "Curve value must be finite."));

            if (double.IsFinite(point.Time) && point.Time < 0.0)
                diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.NegativeTime, i, "Curve time must be nonnegative."));

            if (i > 0)
            {
                var previous = points[i - 1];
                if (double.IsFinite(point.Time) && double.IsFinite(previous.Time) && point.Time <= previous.Time)
                    diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.DuplicateOrUnorderedTime, i, "Curve times must be strictly increasing."));
            }
        }

        if (requireTimeZero && points.Count > 0 && points[0].Time != 0.0)
            diagnostics.Add(new CurveDiagnostic(CurveDiagnosticCode.MissingTimeZero, 0, "Curve must start at time zero."));

        return new CurveValidationResult(diagnostics);
    }
}
