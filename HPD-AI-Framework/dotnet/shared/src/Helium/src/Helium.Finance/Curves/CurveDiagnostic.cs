namespace Helium.Finance.Curves;

public readonly record struct CurveDiagnostic
{
    public CurveDiagnostic(CurveDiagnosticCode Code, int Index, string Message)
    {
        if (Code is not (CurveDiagnosticCode.EmptyCurve
            or CurveDiagnosticCode.NonFiniteTime
            or CurveDiagnosticCode.NonFiniteValue
            or CurveDiagnosticCode.NegativeTime
            or CurveDiagnosticCode.DuplicateOrUnorderedTime
            or CurveDiagnosticCode.MissingTimeZero
            or CurveDiagnosticCode.InvalidTimeZeroDiscountFactor
            or CurveDiagnosticCode.NonPositiveDiscountFactor
            or CurveDiagnosticCode.IncreasingDiscountFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(Code), Code, "Unsupported curve diagnostic code.");
        }

        if (string.IsNullOrWhiteSpace(Message))
            throw new ArgumentException("Diagnostic message must not be empty.", nameof(Message));

        if (Index < -1)
            throw new ArgumentOutOfRangeException(nameof(Index), Index, "Diagnostic index must be -1 or nonnegative.");

        this.Code = Code;
        this.Index = Index;
        this.Message = Message;
    }

    public CurveDiagnosticCode Code { get; }

    public int Index { get; }

    public string Message { get; }

    public void Deconstruct(out CurveDiagnosticCode Code, out int Index, out string Message)
    {
        Code = this.Code;
        Index = this.Index;
        Message = this.Message;
    }
}
