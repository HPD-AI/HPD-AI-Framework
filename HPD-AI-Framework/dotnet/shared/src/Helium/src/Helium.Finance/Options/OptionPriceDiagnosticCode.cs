namespace Helium.Finance.Options;

public enum OptionPriceDiagnosticCode
{
    InvalidInput,
    NonFinitePrice,
    NonFiniteBound,
    BelowLowerBound,
    AboveUpperBound,
    PutCallParityViolation,
    InvalidTolerance
}
