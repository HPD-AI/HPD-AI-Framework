using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free kernels over canonical multiset views.
/// </summary>
public static class MultisetKernels
{
    public static AlgebraStatus ValidateCanonical<T, TOrder>(
        MultisetView<T> multiset,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        var status = multiset.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        for (var i = 0; i < multiset.Count; i++)
        {
            if (multiset.CountAt(i) <= 0)
                return AlgebraStatus.InvalidInput;

            if (i == 0)
                continue;

            if (order.Compare(multiset.ElementAt(i - 1), multiset.ElementAt(i)) != Ordering.Less)
                return AlgebraStatus.InvalidInput;
        }

        return AlgebraStatus.Ok;
    }

    public static int Multiplicity<T, TOrder>(
        MultisetView<T> multiset,
        in T element,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        var index = IndexOf(multiset, element, order);
        return index < 0 ? 0 : multiset.CountAt(index);
    }

    public static int IndexOf<T, TOrder>(
        MultisetView<T> multiset,
        in T element,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        var low = 0;
        var high = multiset.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var compare = order.Compare(multiset.ElementAt(mid), element);
            if (compare == Ordering.Equal)
                return mid;
            if (compare == Ordering.Less)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return -1;
    }

    public static AlgebraStatus TryUnion<T, TOrder>(
        MultisetView<T> left,
        MultisetView<T> right,
        ref MultisetBuilder<T> destination,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        return TryMerge(left, right, ref destination, order, MergeKind.Union);
    }

    public static AlgebraStatus TryIntersect<T, TOrder>(
        MultisetView<T> left,
        MultisetView<T> right,
        ref MultisetBuilder<T> destination,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        return TryMerge(left, right, ref destination, order, MergeKind.Intersect);
    }

    public static AlgebraStatus TrySum<T, TOrder>(
        MultisetView<T> left,
        MultisetView<T> right,
        ref MultisetBuilder<T> destination,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        return TryMerge(left, right, ref destination, order, MergeKind.Sum);
    }

    private static AlgebraStatus TryMerge<T, TOrder>(
        MultisetView<T> left,
        MultisetView<T> right,
        ref MultisetBuilder<T> destination,
        TOrder order,
        MergeKind kind)
        where TOrder : struct, ITotalOrderOps<T>
    {
        var status = ValidateCanonical(left, order);
        if (status != AlgebraStatus.Ok)
            return status;

        status = ValidateCanonical(right, order);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        var i = 0;
        var j = 0;
        while (i < left.Count && j < right.Count)
        {
            var compare = order.Compare(left.ElementAt(i), right.ElementAt(j));
            if (compare == Ordering.Less)
            {
                if (kind != MergeKind.Intersect)
                    status = destination.TryAppend(left.ElementAt(i), left.CountAt(i));
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                if (kind != MergeKind.Intersect)
                    status = destination.TryAppend(right.ElementAt(j), right.CountAt(j));
                j++;
            }
            else
            {
                if (kind == MergeKind.Sum && int.MaxValue - left.CountAt(i) < right.CountAt(j))
                    return AlgebraStatus.Overflow;

                var count = kind switch
                {
                    MergeKind.Union => System.Math.Max(left.CountAt(i), right.CountAt(j)),
                    MergeKind.Intersect => System.Math.Min(left.CountAt(i), right.CountAt(j)),
                    _ => left.CountAt(i) + right.CountAt(j)
                };

                status = destination.TryAppend(left.ElementAt(i), count);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        if (kind == MergeKind.Intersect)
            return AlgebraStatus.Ok;

        while (i < left.Count)
        {
            status = destination.TryAppend(left.ElementAt(i), left.CountAt(i));
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.Count)
        {
            status = destination.TryAppend(right.ElementAt(j), right.CountAt(j));
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    private enum MergeKind
    {
        Union,
        Intersect,
        Sum
    }
}
