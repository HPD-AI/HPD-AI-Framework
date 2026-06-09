using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free helpers for executable finite enumerations.
/// </summary>
public static class FiniteEnumerationKernels
{
    public static AlgebraStatus TryFill<T, TEnumeration>(
        Span<T> destination,
        TEnumeration enumeration)
        where TEnumeration : struct, IFiniteEnumerationOps<T>
    {
        return enumeration.TryFill(destination);
    }

    public static AlgebraStatus TryAsList<T, TEnumeration>(
        ref FiniteListBuilder<T> destination,
        TEnumeration enumeration)
        where TEnumeration : struct, IFiniteEnumerationOps<T>
    {
        if (destination.Capacity < enumeration.Cardinality)
            return AlgebraStatus.InsufficientDestination;

        destination.Clear();
        for (var i = 0; i < enumeration.Cardinality; i++)
        {
            var status = enumeration.TryGetElement(i, out var value);
            if (status != AlgebraStatus.Ok)
                return status;

            status = destination.TryAppend(value);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static bool Contains<T, TEnumeration>(
        in T value,
        TEnumeration enumeration)
        where TEnumeration : struct, IFiniteEnumerationOps<T>
    {
        return IndexOf(value, enumeration) >= 0;
    }

    public static int IndexOf<T, TEnumeration>(
        in T value,
        TEnumeration enumeration)
        where TEnumeration : struct, IFiniteEnumerationOps<T>
    {
        for (var i = 0; i < enumeration.Cardinality; i++)
        {
            var status = enumeration.TryGetElement(i, out var candidate);
            if (status != AlgebraStatus.Ok)
                return -1;

            if (enumeration.Eq(candidate, value))
                return i;
        }

        return -1;
    }
}
