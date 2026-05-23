namespace Helium.Primitives.Tests;

public class FiniteListTests
{
    [Fact]
    public void EmptyListHasLengthZero()
    {
        var list = FiniteList<Integer>.Empty;
        Assert.True(list.IsEmpty);
        Assert.Equal(0, list.Length);
    }

    [Fact]
    public void ConsPrepends()
    {
        var list = FiniteList<Integer>.Of(2, 3).Cons(1);
        Assert.Equal(FiniteList<Integer>.Of(1, 2, 3), list);
    }

    [Fact]
    public void AppendConcatenates()
    {
        var left = FiniteList<Integer>.Of(1, 2);
        var right = FiniteList<Integer>.Of(3, 4);
        Assert.Equal(FiniteList<Integer>.Of(1, 2, 3, 4), left.Append(right));
    }

    [Fact]
    public void ReverseReversesOrder()
    {
        Assert.Equal(FiniteList<Integer>.Of(3, 2, 1), FiniteList<Integer>.Of(1, 2, 3).Reverse());
    }

    [Fact]
    public void HeadAndTail()
    {
        var list = FiniteList<Integer>.Of(1, 2, 3);
        Assert.Equal((Integer)1, list.Head);
        Assert.Equal(FiniteList<Integer>.Of(2, 3), list.Tail);
    }

    [Fact]
    public void EmptyHeadThrows()
    {
        Assert.Throws<InvalidOperationException>(() => FiniteList<Integer>.Empty.Head);
        Assert.Throws<InvalidOperationException>(() => FiniteList<Integer>.Empty.Tail);
    }

    [Fact]
    public void MapFilterAndFold()
    {
        var list = FiniteList<Integer>.Of(1, 2, 3, 4);
        var mapped = list.Map(x => x * (Integer)2);
        var filtered = mapped.Filter(x => x > (Integer)4);
        var sum = filtered.FoldLeft(Integer.Zero, (acc, x) => acc + x);

        Assert.Equal(FiniteList<Integer>.Of(6, 8), filtered);
        Assert.Equal((Integer)14, sum);
    }

    [Fact]
    public void ZipStopsAtShorterList()
    {
        var left = FiniteList<Integer>.Of(1, 2, 3);
        var right = FiniteList<Nat>.Of(4, 5);
        var zipped = left.Zip(right);

        Assert.Equal(2, zipped.Length);
        Assert.Equal(new Pair<Integer, Nat>(1, 4), zipped[0]);
        Assert.Equal(new Pair<Integer, Nat>(2, 5), zipped[1]);
    }

    [Fact]
    public void ContainsIndexOfAndUniqueUseDecidableEquality()
    {
        var list = FiniteList<Integer>.Of(1, 2, 1, 3);
        Assert.True(list.Contains(2));
        Assert.Equal(1, list.IndexOf(2));
        Assert.Equal(-1, list.IndexOf(99));
        Assert.Equal(FiniteList<Integer>.Of(1, 2, 3), list.Unique());
    }

    [Fact]
    public void SortUsesHeliumTotalOrder()
    {
        var list = FiniteList<Integer>.Of(3, 1, 2);
        Assert.Equal(FiniteList<Integer>.Of(1, 2, 3), list.Sort());
    }
}
