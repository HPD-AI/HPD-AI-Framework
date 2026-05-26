namespace Helium.Finance.Curves;

public enum CurveDiagnosticCode
{
    EmptyCurve,
    NonFiniteTime,
    NonFiniteValue,
    NegativeTime,
    DuplicateOrUnorderedTime,
    MissingTimeZero,
    InvalidTimeZeroDiscountFactor,
    NonPositiveDiscountFactor,
    IncreasingDiscountFactor
}
