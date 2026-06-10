using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free finite folds for complete finite lattice witnesses.
/// </summary>
public static class CompleteFiniteLatticeKernels
{
    public static AlgebraStatus TrySupremum<T, TOps>(
        FiniteListView<T> values,
        ref T destination,
        TOps ops)
        where TOps : struct, ICompleteFiniteLatticeOps<T>
    {
        return ops.TrySupremum(ref destination, values.Items);
    }

    public static AlgebraStatus TryInfimum<T, TOps>(
        FiniteListView<T> values,
        ref T destination,
        TOps ops)
        where TOps : struct, ICompleteFiniteLatticeOps<T>
    {
        return ops.TryInfimum(ref destination, values.Items);
    }
}
