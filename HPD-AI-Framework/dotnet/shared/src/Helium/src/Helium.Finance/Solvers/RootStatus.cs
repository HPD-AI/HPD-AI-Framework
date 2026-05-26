namespace Helium.Finance.Solvers;

public enum RootStatus
{
    Converged,
    NoBracket,
    MaxIterations,
    NonFiniteInput,
    NonFiniteFunctionValue,
    FlatDerivative
}
