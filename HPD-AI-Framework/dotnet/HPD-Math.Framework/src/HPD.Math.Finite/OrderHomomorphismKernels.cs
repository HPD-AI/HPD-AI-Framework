using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Finite validation helpers for executable order homomorphism witnesses.
/// </summary>
public static class OrderHomomorphismKernels
{
    public static AlgebraStatus TryValidateMonotone<TSource, TTarget, THom, TSourceEnumeration, TSourceOrder, TTargetOrder>(
        THom hom,
        TSourceEnumeration sourceEnumeration,
        TSourceOrder sourceOrder,
        TTargetOrder targetOrder)
        where THom : struct, IOrderHomOps<TSource, TTarget>
        where TSourceEnumeration : struct, IFiniteEnumerationOps<TSource>
        where TSourceOrder : struct, IPartialOrderOps<TSource>
        where TTargetOrder : struct, IPartialOrderOps<TTarget>
    {
        for (var i = 0; i < sourceEnumeration.Cardinality; i++)
        {
            var leftStatus = sourceEnumeration.TryGetElement(i, out var left);
            if (leftStatus != AlgebraStatus.Ok)
                return leftStatus;

            for (var j = 0; j < sourceEnumeration.Cardinality; j++)
            {
                var rightStatus = sourceEnumeration.TryGetElement(j, out var right);
                if (rightStatus != AlgebraStatus.Ok)
                    return rightStatus;

                if (!sourceOrder.LessEqual(left, right))
                    continue;

                TTarget leftImage = default!;
                TTarget rightImage = default!;
                hom.Apply(ref leftImage, left);
                hom.Apply(ref rightImage, right);

                if (!targetOrder.LessEqual(leftImage, rightImage))
                    return AlgebraStatus.InvalidInput;
            }
        }

        return AlgebraStatus.Ok;
    }
}
