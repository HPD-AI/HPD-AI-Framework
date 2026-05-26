namespace Helium.Finance.Options;

public readonly record struct OptionPriceDiagnostic
{
    public OptionPriceDiagnostic(OptionPriceDiagnosticCode Code, string Message)
    {
        if (Code is not (OptionPriceDiagnosticCode.InvalidInput
            or OptionPriceDiagnosticCode.NonFinitePrice
            or OptionPriceDiagnosticCode.NonFiniteBound
            or OptionPriceDiagnosticCode.BelowLowerBound
            or OptionPriceDiagnosticCode.AboveUpperBound
            or OptionPriceDiagnosticCode.PutCallParityViolation
            or OptionPriceDiagnosticCode.InvalidTolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(Code), Code, "Unsupported option price diagnostic code.");
        }

        if (string.IsNullOrWhiteSpace(Message))
            throw new ArgumentException("Diagnostic message must not be empty.", nameof(Message));

        this.Code = Code;
        this.Message = Message;
    }

    public OptionPriceDiagnosticCode Code { get; }

    public string Message { get; }

    public void Deconstruct(out OptionPriceDiagnosticCode Code, out string Message)
    {
        Code = this.Code;
        Message = this.Message;
    }
}
