using System.Collections.Generic;

namespace Helium.Primitives;

/// <summary>
/// Adapter from Helium total order to BCL sorted collection comparers.
/// </summary>
internal sealed class TotalOrderComparer<T> : IComparer<T>
    where T : ITotalOrder<T>
{
    public static TotalOrderComparer<T> Instance { get; } = new();

    private TotalOrderComparer()
    {
    }

    public int Compare(T? x, T? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return T.CompareOrder(x, y) switch
        {
            Ordering.Less => -1,
            Ordering.Equal => 0,
            Ordering.Greater => 1,
            _ => throw new InvalidOperationException("Invalid ordering result.")
        };
    }
}
