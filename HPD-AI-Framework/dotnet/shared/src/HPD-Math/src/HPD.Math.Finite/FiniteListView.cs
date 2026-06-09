using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Non-owning finite list view. Order and duplicates are meaningful.
/// </summary>
public readonly ref struct FiniteListView<T>
{
    public FiniteListView(ReadOnlySpan<T> items)
    {
        Items = items;
    }

    public ReadOnlySpan<T> Items { get; }

    public int Count => Items.Length;

    public bool IsEmpty => Count == 0;

    public T this[int index] => Items[index];
}

public static class FiniteListViewExtensions
{
    extension<T>(FiniteListView<T> self)
    {
        public bool Contains<TOps>(in T value, TOps ops)
            where TOps : struct, IEqualityOps<T>
        {
            return FiniteListKernels.Contains(self, value, ops);
        }

        public int IndexOf<TOps>(in T value, TOps ops)
            where TOps : struct, IEqualityOps<T>
        {
            return FiniteListKernels.IndexOf(self, value, ops);
        }
    }
}
