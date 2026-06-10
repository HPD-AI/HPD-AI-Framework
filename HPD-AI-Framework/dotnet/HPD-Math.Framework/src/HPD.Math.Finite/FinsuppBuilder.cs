using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Caller-owned builder for canonical finite-support data.
/// </summary>
public ref struct FinsuppBuilder<TKey, TValue>
{
    private readonly Span<TKey> _keys;
    private readonly Span<TValue> _values;
    private int _count;

    public FinsuppBuilder(Span<TKey> keys, Span<TValue> values)
    {
        _keys = keys;
        _values = values;
        _count = 0;
    }

    public int Count => _count;

    public int Capacity => System.Math.Min(_keys.Length, _values.Length);

    public void Clear() => _count = 0;

    public AlgebraStatus TryAppend(in TKey key, in TValue value)
    {
        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        _keys[_count] = key;
        _values[_count] = value;
        _count++;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryAppendCanonical<TKeyOrder, TValueOps>(
        in TKey key,
        in TValue value,
        TKeyOrder keyOrder,
        TValueOps valueOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TValueOps : struct, IAdditiveCommutativeMonoidOps<TValue>
    {
        if (valueOps.Eq(value, valueOps.Zero))
            return AlgebraStatus.Ok;

        if (_count > 0 && keyOrder.Compare(_keys[_count - 1], key) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        return TryAppend(key, value);
    }

    public AlgebraStatus TryAppendCanonicalStatus<TKeyOrder, TValueOps>(
        in TKey key,
        in TValue value,
        TKeyOrder keyOrder,
        TValueOps valueOps)
        where TKeyOrder : struct, ITotalOrderOps<TKey>
        where TValueOps : struct, IStatusRingOps<TValue>
    {
        if (valueOps.Eq(value, valueOps.Zero))
            return AlgebraStatus.Ok;

        if (_count > 0 && keyOrder.Compare(_keys[_count - 1], key) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        return TryAppend(key, value);
    }

    public FinsuppView<TKey, TValue> AsView() =>
        new(_keys[.._count], _values[.._count]);
}
