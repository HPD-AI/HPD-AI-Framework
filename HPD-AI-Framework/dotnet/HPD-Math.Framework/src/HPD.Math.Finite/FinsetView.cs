using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Non-owning finite set view. Canonical views are strictly sorted with no duplicates.
/// </summary>
public readonly ref struct FinsetView<T>
{
    public FinsetView(ReadOnlySpan<T> elements)
    {
        Elements = elements;
    }

    public ReadOnlySpan<T> Elements { get; }

    public int Count => Elements.Length;

    public bool IsEmpty => Count == 0;

    public T this[int index] => Elements[index];
}

public static class FinsetViewExtensions
{
    extension<T>(FinsetView<T> self)
    {
        public AlgebraStatus ValidateCanonical<TOrder>(TOrder order)
            where TOrder : struct, ITotalOrderOps<T>
        {
            return FinsetKernels.ValidateCanonical(self, order);
        }

        public bool Contains<TOrder>(in T value, TOrder order)
            where TOrder : struct, ITotalOrderOps<T>
        {
            return FinsetKernels.Contains(self, value, order);
        }
    }
}
