using System.Collections;

namespace Helium.Primitives;

/// <summary>
/// Enumerates pairs (0,n), (1,n-1), ..., (n,0).
/// </summary>
public readonly struct NatAntidiagonal : IEnumerable<Pair<Nat, Nat>>
{
    public NatAntidiagonal(int sum)
    {
        if (sum < 0)
            throw new ArgumentOutOfRangeException(nameof(sum), "Antidiagonal sum must be nonnegative.");
        Sum = new Nat(sum);
    }

    public Nat Sum { get; }
    public int Count => Sum.Value + 1;

    public IEnumerator<Pair<Nat, Nat>> GetEnumerator()
    {
        for (int left = 0; left <= Sum.Value; left++)
            yield return new Pair<Nat, Nat>(new Nat(left), new Nat(Sum.Value - left));
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
