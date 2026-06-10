using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free kernels over canonical finite set views.
/// </summary>
public static class FinsetKernels
{
    public static AlgebraStatus ValidateCanonical<T, TOrder>(
        FinsetView<T> set,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        for (var i = 1; i < set.Count; i++)
            if (order.Compare(set[i - 1], set[i]) != Ordering.Less)
                return AlgebraStatus.InvalidInput;

        return AlgebraStatus.Ok;
    }

    public static bool Contains<T, TOrder>(
        FinsetView<T> set,
        in T value,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        return IndexOf(set, value, order) >= 0;
    }

    public static int IndexOf<T, TOrder>(
        FinsetView<T> set,
        in T value,
        TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        var low = 0;
        var high = set.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var compare = order.Compare(set[mid], value);
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
        FinsetView<T> left,
        FinsetView<T> right,
        ref FinsetBuilder<T> destination,
        TOrder order)
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
            var compare = order.Compare(left[i], right[j]);
            if (compare == Ordering.Less)
            {
                status = destination.TryAppend(left[i]);
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                status = destination.TryAppend(right[j]);
                j++;
            }
            else
            {
                status = destination.TryAppend(left[i]);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.Count)
        {
            status = destination.TryAppend(left[i]);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.Count)
        {
            status = destination.TryAppend(right[j]);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryIntersect<T, TOrder>(
        FinsetView<T> left,
        FinsetView<T> right,
        ref FinsetBuilder<T> destination,
        TOrder order)
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
            var compare = order.Compare(left[i], right[j]);
            if (compare == Ordering.Less)
            {
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                j++;
            }
            else
            {
                status = destination.TryAppend(left[i]);
                if (status != AlgebraStatus.Ok)
                    return status;
                i++;
                j++;
            }
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryExcept<T, TOrder>(
        FinsetView<T> left,
        FinsetView<T> right,
        ref FinsetBuilder<T> destination,
        TOrder order)
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
        while (i < left.Count)
        {
            if (j >= right.Count)
            {
                status = destination.TryAppend(left[i]);
                if (status != AlgebraStatus.Ok)
                    return status;
                i++;
                continue;
            }

            var compare = order.Compare(left[i], right[j]);
            if (compare == Ordering.Less)
            {
                status = destination.TryAppend(left[i]);
                if (status != AlgebraStatus.Ok)
                    return status;
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }

        return AlgebraStatus.Ok;
    }
}
