using HPD.Math.Core;

namespace HPD.Math.Finite;

/// <summary>
/// Non-owning multiset view. Canonical views have strictly sorted elements and positive counts.
/// </summary>
public readonly ref struct MultisetView<T>
{
    public MultisetView(ReadOnlySpan<T> elements, ReadOnlySpan<int> counts)
    {
        Elements = elements;
        Counts = counts;
    }

    public ReadOnlySpan<T> Elements { get; }

    public ReadOnlySpan<int> Counts { get; }

    public int Count => Elements.Length;

    public bool IsEmpty => Count == 0;

    public AlgebraStatus ValidateShape() =>
        Elements.Length == Counts.Length ? AlgebraStatus.Ok : AlgebraStatus.InvalidInput;

    public T ElementAt(int supportIndex) => Elements[supportIndex];

    public int CountAt(int supportIndex) => Counts[supportIndex];
}

public static class MultisetViewExtensions
{
    extension<T>(MultisetView<T> self)
    {
        public AlgebraStatus ValidateCanonical<TOrder>(TOrder order)
            where TOrder : struct, ITotalOrderOps<T>
        {
            return MultisetKernels.ValidateCanonical(self, order);
        }
    }
}
