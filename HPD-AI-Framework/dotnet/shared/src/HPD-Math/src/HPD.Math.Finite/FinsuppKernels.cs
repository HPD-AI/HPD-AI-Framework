using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free kernels over canonical finite-support views.
/// </summary>
public static class FinsuppKernels
{
    public static AlgebraStatus ValidateCanonical<TKey, TValue, TKeyOrder, TValueOps>(
        FinsuppView<TKey, TValue> value,
        TKeyOrder keyOrder,
        TValueOps valueOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
    {
        var status = value.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        for (var i = 0; i < value.Count; i++)
        {
            if (valueOps.Eq(value.Values[i], valueOps.Zero))
                return AlgebraStatus.InvalidInput;

            if (i == 0)
                continue;

            if (keyOrder.Compare(value.Keys[i - 1], value.Keys[i]) != Ordering.Less)
                return AlgebraStatus.InvalidInput;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMapValues<TKey, TInput, TOutput, TKeyOrder, TInputOps, TOutputOps, TMapOps>(
        FinsuppView<TKey, TInput> source,
        ref FinsuppBuilder<TKey, TOutput> destination,
        TKeyOrder keyOrder,
        TInputOps inputOps,
        TOutputOps outputOps,
        TMapOps mapOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TInputOps : struct, IAdditiveCommutativeMonoidOps<TInput>
        where TOutputOps : struct, IAdditiveCommutativeMonoidOps<TOutput>
        where TMapOps : struct, IMapOps<TInput, TOutput>
    {
        var status = ValidateCanonical(source, keyOrder, inputOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var mapped = outputOps.Zero;
            mapOps.Map(ref mapped, source.Values[i]);

            status = destination.TryAppendCanonical(source.Keys[i], mapped, keyOrder, outputOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryRemapKeys<TKey, TNewKey, TValue, TKeyOrder, TNewKeyOrder, TValueOps, TKeyMapOps>(
        FinsuppView<TKey, TValue> source,
        ref FinsuppBuilder<TNewKey, TValue> destination,
        Span<TNewKey> workspaceKeys,
        Span<TValue> workspaceValues,
        TKeyOrder keyOrder,
        TNewKeyOrder newKeyOrder,
        TValueOps valueOps,
        TKeyMapOps keyMapOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TNewKeyOrder : struct, ITotalOrderOps<TNewKey>
        where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
        where TKeyMapOps : struct, IMapOps<TKey, TNewKey>
    {
        var status = ValidateCanonical(source, keyOrder, valueOps);
        if (status != AlgebraStatus.Ok)
            return status;

        if (workspaceKeys.Length < source.Count || workspaceValues.Length < source.Count)
            return AlgebraStatus.InsufficientWorkspace;

        var workspaceCount = 0;
        for (var i = 0; i < source.Count; i++)
        {
            var mappedKey = default(TNewKey)!;
            keyMapOps.Map(ref mappedKey, source.Keys[i]);
            AccumulateMapped(mappedKey, source.Values[i], workspaceKeys, workspaceValues, ref workspaceCount, newKeyOrder, valueOps);
        }

        SortByKey(workspaceKeys[..workspaceCount], workspaceValues[..workspaceCount], newKeyOrder);

        destination.Clear();
        for (var i = 0; i < workspaceCount; i++)
        {
            status = destination.TryAppendCanonical(workspaceKeys[i], workspaceValues[i], newKeyOrder, valueOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static void Fold<TKey, TValue, TAccumulator, TFoldOps>(
        FinsuppView<TKey, TValue> source,
        ref TAccumulator accumulator,
        TFoldOps foldOps)
        where TFoldOps : struct, IFinsuppFoldOps<TKey, TValue, TAccumulator>
    {
        for (var i = 0; i < source.Count; i++)
            foldOps.Step(ref accumulator, source.Keys[i], source.Values[i]);
    }

    public static AlgebraStatus TryAdd<TKey, TValue, TKeyOrder, TValueOps>(
        FinsuppView<TKey, TValue> left,
        FinsuppView<TKey, TValue> right,
        ref FinsuppBuilder<TKey, TValue> destination,
        TKeyOrder keyOrder,
        TValueOps valueOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
    {
        var status = left.ValidateCanonical(keyOrder, valueOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonical(keyOrder, valueOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();

        var i = 0;
        var j = 0;

        while (i < left.Count && j < right.Count)
        {
            var compare = keyOrder.Compare(left.Keys[i], right.Keys[j]);
            if (compare == Ordering.Less)
            {
                status = destination.TryAppendCanonical(left.Keys[i], left.Values[i], keyOrder, valueOps);
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                status = destination.TryAppendCanonical(right.Keys[j], right.Values[j], keyOrder, valueOps);
                j++;
            }
            else
            {
                var sum = valueOps.Zero;
                valueOps.Add(ref sum, left.Values[i], right.Values[j]);
                status = destination.TryAppendCanonical(left.Keys[i], sum, keyOrder, valueOps);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.Count)
        {
            status = destination.TryAppendCanonical(left.Keys[i], left.Values[i], keyOrder, valueOps);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.Count)
        {
            status = destination.TryAppendCanonical(right.Keys[j], right.Values[j], keyOrder, valueOps);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryNeg<TKey, TValue, TKeyOrder, TValueOps>(
        FinsuppView<TKey, TValue> value,
        ref FinsuppBuilder<TKey, TValue> destination,
        TKeyOrder keyOrder,
        TValueOps valueOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TValueOps : struct, IRingOps<TValue>
    {
        var status = value.ValidateCanonical(keyOrder, valueOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();

        for (var i = 0; i < value.Count; i++)
        {
            var negated = valueOps.Zero;
            valueOps.Neg(ref negated, value.Values[i]);

            status = destination.TryAppendCanonical(value.Keys[i], negated, keyOrder, valueOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    private static void AccumulateMapped<TKey, TValue, TKeyOrder, TValueOps>(
        in TKey key,
        in TValue value,
        Span<TKey> workspaceKeys,
        Span<TValue> workspaceValues,
        ref int workspaceCount,
        TKeyOrder keyOrder,
        TValueOps valueOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
    {
        if (valueOps.Eq(value, valueOps.Zero))
            return;

        for (var i = 0; i < workspaceCount; i++)
        {
            if (keyOrder.Compare(workspaceKeys[i], key) != Ordering.Equal)
                continue;

            var sum = valueOps.Zero;
            valueOps.Add(ref sum, workspaceValues[i], value);
            workspaceValues[i] = sum;
            return;
        }

        workspaceKeys[workspaceCount] = key;
        workspaceValues[workspaceCount] = value;
        workspaceCount++;
    }

    private static void SortByKey<TKey, TValue, TKeyOrder>(
        Span<TKey> keys,
        Span<TValue> values,
        TKeyOrder keyOrder)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
    {
        for (var i = 1; i < keys.Length; i++)
        {
            var key = keys[i];
            var value = values[i];
            var j = i - 1;

            while (j >= 0 && keyOrder.Compare(keys[j], key) == Ordering.Greater)
            {
                keys[j + 1] = keys[j];
                values[j + 1] = values[j];
                j--;
            }

            keys[j + 1] = key;
            values[j + 1] = value;
        }
    }
}

/// <summary>
/// C# extension-block convenience wrappers over the explicit kernels.
/// </summary>
public static class FinsuppKernelExtensions
{
    extension<TKey, TValue>(FinsuppView<TKey, TValue> self)
    {
        public AlgebraStatus TryAdd<TKeyOrder, TValueOps>(
            FinsuppView<TKey, TValue> other,
            ref FinsuppBuilder<TKey, TValue> destination,
            TKeyOrder keyOrder,
            TValueOps valueOps)
            where TKeyOrder : struct, ITotalOrderOps<TKey>
            where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
        {
            return FinsuppKernels.TryAdd(self, other, ref destination, keyOrder, valueOps);
        }
    }
}
