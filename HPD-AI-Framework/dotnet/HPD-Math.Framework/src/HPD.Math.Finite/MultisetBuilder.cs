using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Caller-owned builder for canonical multisets.
/// </summary>
public ref struct MultisetBuilder<T>
{
    private readonly Span<T> _elements;
    private readonly Span<int> _counts;
    private int _count;

    public MultisetBuilder(Span<T> elements, Span<int> counts)
    {
        _elements = elements;
        _counts = counts;
        _count = 0;
    }

    public int Count => _count;

    public int Capacity => System.Math.Min(_elements.Length, _counts.Length);

    public void Clear() => _count = 0;

    public AlgebraStatus TryAppend(in T element, int count)
    {
        if (count <= 0)
            return AlgebraStatus.InvalidInput;

        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        _elements[_count] = element;
        _counts[_count] = count;
        _count++;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryAppendCanonical<TOrder>(in T element, int count, TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        if (count <= 0)
            return AlgebraStatus.Ok;

        if (_count > 0 && order.Compare(_elements[_count - 1], element) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        return TryAppend(element, count);
    }

    public MultisetView<T> AsView() => new(_elements[.._count], _counts[.._count]);
}
