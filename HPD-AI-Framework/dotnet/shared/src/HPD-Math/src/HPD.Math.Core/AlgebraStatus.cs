namespace HPD.Math.Core;

/// <summary>
/// Result code used by kernel APIs instead of hot-path exceptions.
/// </summary>
public enum AlgebraStatus
{
    Ok = 0,
    InvalidInput,
    InsufficientDestination,
    InsufficientWorkspace,
    DimensionMismatch,
    IncompatibleContext,
    DivisionByZero,
    NonInvertible,
    Overflow
}
