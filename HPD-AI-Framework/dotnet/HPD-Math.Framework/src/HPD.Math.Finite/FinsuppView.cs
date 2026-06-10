using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Non-owning view of a finitely supported function. Canonical views use sorted unique keys
/// and omit entries whose value is the additive identity.
/// </summary>
public readonly ref struct FinsuppView<TKey, TValue>
{
    public FinsuppView(ReadOnlySpan<TKey> keys, ReadOnlySpan<TValue> values)
    {
        Keys = keys;
        Values = values;
    }

    public ReadOnlySpan<TKey> Keys { get; }

    public ReadOnlySpan<TValue> Values { get; }

    public int Count => Keys.Length;

    public bool IsZero => Count == 0;

    public AlgebraStatus ValidateShape() =>
        Keys.Length == Values.Length ? AlgebraStatus.Ok : AlgebraStatus.InvalidInput;
}

/// <summary>
/// C# extension-block convenience over finite-support views. These remain explicit and non-allocating.
/// </summary>
public static class FinsuppViewExtensions
{
    extension<TKey, TValue>(FinsuppView<TKey, TValue> self)
    {
        public AlgebraStatus ValidateCanonical<TKeyOrder, TValueOps>(
            TKeyOrder keyOrder,
            TValueOps valueOps)
            where TKeyOrder : struct, ITotalOrderOps<TKey>
            where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
        {
            return FinsuppKernels.ValidateCanonical(self, keyOrder, valueOps);
        }

        public AlgebraStatus ValidateCanonicalStatus<TKeyOrder, TValueOps>(
            TKeyOrder keyOrder,
            TValueOps valueOps)
            where TKeyOrder : struct, ITotalOrderOps<TKey>
            where TValueOps : struct, IStatusRingOps<TValue>
        {
            var status = self.ValidateShape();
            if (status != AlgebraStatus.Ok)
                return status;

            for (var i = 0; i < self.Count; i++)
            {
                if (valueOps.Eq(self.Values[i], valueOps.Zero))
                    return AlgebraStatus.InvalidInput;

                if (i > 0 && keyOrder.Compare(self.Keys[i - 1], self.Keys[i]) != Ordering.Less)
                    return AlgebraStatus.InvalidInput;
            }

            return AlgebraStatus.Ok;
        }
    }
}
