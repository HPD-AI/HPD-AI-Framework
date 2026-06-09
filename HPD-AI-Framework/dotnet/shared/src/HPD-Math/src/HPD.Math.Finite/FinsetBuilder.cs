using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Caller-owned builder for canonical finite sets.
/// </summary>
public ref struct FinsetBuilder<T>
{
    private readonly Span<T> _elements;
    private int _count;

    public FinsetBuilder(Span<T> elements)
    {
        _elements = elements;
        _count = 0;
    }

    public int Count => _count;

    public int Capacity => _elements.Length;

    public void Clear() => _count = 0;

    public AlgebraStatus TryAppend(in T element)
    {
        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        _elements[_count] = element;
        _count++;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryAppendCanonical<TOrder>(in T element, TOrder order)
        where TOrder : struct, ITotalOrderOps<T>
    {
        if (_count > 0 && order.Compare(_elements[_count - 1], element) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        return TryAppend(element);
    }

    public FinsetView<T> AsView() => new(_elements[.._count]);
}
