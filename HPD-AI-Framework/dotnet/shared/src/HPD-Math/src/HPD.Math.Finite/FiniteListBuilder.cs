using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Caller-owned builder for finite lists.
/// </summary>
public ref struct FiniteListBuilder<T>
{
    private readonly Span<T> _items;
    private int _count;

    public FiniteListBuilder(Span<T> items)
    {
        _items = items;
        _count = 0;
    }

    public int Count => _count;

    public int Capacity => _items.Length;

    public void Clear() => _count = 0;

    public AlgebraStatus TryAppend(in T item)
    {
        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        _items[_count] = item;
        _count++;
        return AlgebraStatus.Ok;
    }

    public FiniteListView<T> AsView() => new(_items[.._count]);
}
