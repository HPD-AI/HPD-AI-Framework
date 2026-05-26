namespace Helium.Finance.Options;

public enum ImpliedVolatilityStatus
{
    Converged,
    BelowIntrinsic,
    AboveUpperBound,
    NoBracket,
    MaxIterations,
    NonFiniteInput,
    NonFiniteFunctionValue,
    FlatVega
}
