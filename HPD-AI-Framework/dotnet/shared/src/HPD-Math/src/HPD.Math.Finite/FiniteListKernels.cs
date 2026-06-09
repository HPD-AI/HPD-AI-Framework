using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free kernels over finite list views.
/// </summary>
public static class FiniteListKernels
{
    public static AlgebraStatus TryCopy<T>(
        FiniteListView<T> source,
        ref FiniteListBuilder<T> destination)
    {
        destination.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var status = destination.TryAppend(source[i]);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryReverse<T>(
        FiniteListView<T> source,
        ref FiniteListBuilder<T> destination)
    {
        destination.Clear();
        for (var i = source.Count - 1; i >= 0; i--)
        {
            var status = destination.TryAppend(source[i]);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMap<TInput, TOutput, TMapOps>(
        FiniteListView<TInput> source,
        ref FiniteListBuilder<TOutput> destination,
        TMapOps mapOps)
        where TMapOps : struct, IMapOps<TInput, TOutput>
    {
        destination.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var mapped = default(TOutput)!;
            mapOps.Map(ref mapped, source[i]);

            var status = destination.TryAppend(mapped);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static void Fold<TElement, TAccumulator, TFoldOps>(
        FiniteListView<TElement> source,
        ref TAccumulator accumulator,
        TFoldOps foldOps)
        where TFoldOps : struct, IListFoldOps<TElement, TAccumulator>
    {
        for (var i = 0; i < source.Count; i++)
            foldOps.Step(ref accumulator, source[i]);
    }

    public static bool Contains<T, TOps>(
        FiniteListView<T> source,
        in T value,
        TOps ops)
        where TOps : struct, IEqualityOps<T>
    {
        return IndexOf(source, value, ops) >= 0;
    }

    public static int IndexOf<T, TOps>(
        FiniteListView<T> source,
        in T value,
        TOps ops)
        where TOps : struct, IEqualityOps<T>
    {
        for (var i = 0; i < source.Count; i++)
            if (ops.Eq(source[i], value))
                return i;

        return -1;
    }
}
