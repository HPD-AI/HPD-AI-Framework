namespace Helium.Finance.Volatility;

public enum VolatilitySurfaceDiagnosticCode
{
    EmptyTimeAxis,
    EmptyStrikeAxis,
    DimensionMismatch,
    NonFiniteTime,
    NegativeTime,
    DuplicateOrUnorderedTime,
    NonFiniteStrike,
    NegativeStrike,
    NonPositiveStrike,
    DuplicateOrUnorderedStrike,
    NonFiniteVolatility,
    NegativeVolatility,
    DecreasingTotalVariance
}
