namespace Helium.Finance.Volatility;

public readonly record struct VolatilitySurfaceDiagnostic
{
    public VolatilitySurfaceDiagnostic(
        VolatilitySurfaceDiagnosticCode Code,
        int TimeIndex,
        int StrikeIndex,
        string Message)
    {
        if (Code is not (VolatilitySurfaceDiagnosticCode.EmptyTimeAxis
            or VolatilitySurfaceDiagnosticCode.EmptyStrikeAxis
            or VolatilitySurfaceDiagnosticCode.DimensionMismatch
            or VolatilitySurfaceDiagnosticCode.NonFiniteTime
            or VolatilitySurfaceDiagnosticCode.NegativeTime
            or VolatilitySurfaceDiagnosticCode.DuplicateOrUnorderedTime
            or VolatilitySurfaceDiagnosticCode.NonFiniteStrike
            or VolatilitySurfaceDiagnosticCode.NegativeStrike
            or VolatilitySurfaceDiagnosticCode.NonPositiveStrike
            or VolatilitySurfaceDiagnosticCode.DuplicateOrUnorderedStrike
            or VolatilitySurfaceDiagnosticCode.NonFiniteVolatility
            or VolatilitySurfaceDiagnosticCode.NegativeVolatility
            or VolatilitySurfaceDiagnosticCode.DecreasingTotalVariance))
        {
            throw new ArgumentOutOfRangeException(nameof(Code), Code, "Unsupported volatility surface diagnostic code.");
        }

        if (string.IsNullOrWhiteSpace(Message))
            throw new ArgumentException("Diagnostic message must not be empty.", nameof(Message));

        if (TimeIndex < -1)
            throw new ArgumentOutOfRangeException(nameof(TimeIndex), TimeIndex, "Diagnostic time index must be -1 or nonnegative.");

        if (StrikeIndex < -1)
            throw new ArgumentOutOfRangeException(nameof(StrikeIndex), StrikeIndex, "Diagnostic strike index must be -1 or nonnegative.");

        this.Code = Code;
        this.TimeIndex = TimeIndex;
        this.StrikeIndex = StrikeIndex;
        this.Message = Message;
    }

    public VolatilitySurfaceDiagnosticCode Code { get; }

    public int TimeIndex { get; }

    public int StrikeIndex { get; }

    public string Message { get; }

    public void Deconstruct(
        out VolatilitySurfaceDiagnosticCode Code,
        out int TimeIndex,
        out int StrikeIndex,
        out string Message)
    {
        Code = this.Code;
        TimeIndex = this.TimeIndex;
        StrikeIndex = this.StrikeIndex;
        Message = this.Message;
    }
}
