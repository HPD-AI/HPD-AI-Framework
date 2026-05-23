namespace Helium.Primitives.Tests;

public class SeqStreamEnumerationTests
{
    [Fact]
    public void SeqIsReplayable()
    {
        var seq = new Seq<Integer>(() => [1, 2, 3]);

        Assert.Equal(FiniteList<Integer>.Of(1, 2), seq.Take(2));
        Assert.Equal(FiniteList<Integer>.Of(1, 2, 3), seq.Take(3));
    }

    [Fact]
    public void SeqMapFilterDrop()
    {
        var seq = new Seq<Integer>(() => [1, 2, 3, 4, 5])
            .Drop(1)
            .Filter(x => x % 2 == 0)
            .Map(x => x * (Integer)10);

        Assert.Equal(FiniteList<Integer>.Of(20, 40), seq.Take(10));
    }

    [Fact]
    public void StreamTakeMapAndZip()
    {
        static Stream<Nat> From(int n) => new(new Nat(n), () => From(n + 1));

        var naturals = From(0);
        Assert.Equal(FiniteList<Nat>.Of(0, 1, 2, 3), naturals.Take(4));

        var doubled = naturals.Map(n => new Nat(n.Value * 2));
        Assert.Equal(FiniteList<Nat>.Of(0, 2, 4), doubled.Take(3));

        var zipped = naturals.Zip(doubled).Take(3);
        Assert.Equal(new Pair<Nat, Nat>(0, 0), zipped[0]);
        Assert.Equal(new Pair<Nat, Nat>(1, 2), zipped[1]);
        Assert.Equal(new Pair<Nat, Nat>(2, 4), zipped[2]);
    }

    [Fact]
    public void NatAntidiagonalEnumeratesPairs()
    {
        var pairs = new NatAntidiagonal(3).ToArray();

        Assert.Equal(4, pairs.Length);
        Assert.Equal(new Pair<Nat, Nat>(0, 3), pairs[0]);
        Assert.Equal(new Pair<Nat, Nat>(1, 2), pairs[1]);
        Assert.Equal(new Pair<Nat, Nat>(2, 1), pairs[2]);
        Assert.Equal(new Pair<Nat, Nat>(3, 0), pairs[3]);
    }

    [Fact]
    public void NatAntidiagonalRejectsNegativeSum() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NatAntidiagonal(-1));

    [Fact]
    public void ProductEnumeratesCartesianProduct()
    {
        var left = FiniteList<Integer>.Of(1, 2);
        var right = FiniteList<Nat>.Of(3, 4);
        var product = FiniteEnumeration.Product(left, right);

        Assert.Equal(4, product.Length);
        Assert.Equal(new Pair<Integer, Nat>(1, 3), product[0]);
        Assert.Equal(new Pair<Integer, Nat>(1, 4), product[1]);
        Assert.Equal(new Pair<Integer, Nat>(2, 3), product[2]);
        Assert.Equal(new Pair<Integer, Nat>(2, 4), product[3]);
    }

    [Fact]
    public void PermutationsEnumeratesAllOrders()
    {
        var permutations = FiniteEnumeration.Permutations(FiniteList<Integer>.Of(1, 2, 3));

        Assert.Equal(6, permutations.Length);
        Assert.Contains(FiniteList<Integer>.Of(1, 2, 3), permutations);
        Assert.Contains(FiniteList<Integer>.Of(1, 3, 2), permutations);
        Assert.Contains(FiniteList<Integer>.Of(2, 1, 3), permutations);
        Assert.Contains(FiniteList<Integer>.Of(2, 3, 1), permutations);
        Assert.Contains(FiniteList<Integer>.Of(3, 1, 2), permutations);
        Assert.Contains(FiniteList<Integer>.Of(3, 2, 1), permutations);
    }
}
