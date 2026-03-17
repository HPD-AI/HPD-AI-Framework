using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class FinsetTests
{
    // --- Construction ---

    [Fact]
    public void EmptyFinset()
    {
        var s = Finset<int>.Empty;
        Assert.True(s.IsEmpty);
        Assert.Equal(0, s.Card);
    }

    [Fact]
    public void FromElementsDeduplicates()
    {
        var s = Finset<int>.FromElements([1, 2, 1, 3]);
        Assert.Equal(3, s.Card);
    }

    // --- Membership ---

    [Fact]
    public void ContainsPresent()
    {
        var s = Finset<int>.Of(1, 2, 3);
        Assert.True(s.Contains(2));
    }

    [Fact]
    public void ContainsAbsent()
    {
        var s = Finset<int>.Of(1, 2, 3);
        Assert.False(s.Contains(99));
    }

    // --- Insert and Erase ---

    [Fact]
    public void InsertAddsElement()
    {
        var s = Finset<int>.Of(1, 2);
        var s2 = s.Insert(3);
        Assert.Equal(3, s2.Card);
        Assert.True(s2.Contains(3));
    }

    [Fact]
    public void InsertExistingNoChange()
    {
        var s = Finset<int>.Of(1, 2);
        var s2 = s.Insert(1);
        Assert.Equal(2, s2.Card);
    }

    [Fact]
    public void EraseRemovesElement()
    {
        var s = Finset<int>.Of(1, 2, 3);
        var s2 = s.Erase(2);
        Assert.Equal(2, s2.Card);
        Assert.False(s2.Contains(2));
    }

    [Fact]
    public void EraseAbsentNoChange()
    {
        var s = Finset<int>.Of(1, 2);
        var s2 = s.Erase(99);
        Assert.Equal(s, s2);
    }

    // --- Set operations ---

    [Fact]
    public void Union()
    {
        var a = Finset<int>.Of(1, 2);
        var b = Finset<int>.Of(2, 3);
        var u = a.Union(b);
        Assert.Equal(3, u.Card);
        Assert.True(u.Contains(1));
        Assert.True(u.Contains(2));
        Assert.True(u.Contains(3));
    }

    [Fact]
    public void Inter()
    {
        var a = Finset<int>.Of(1, 2, 3);
        var b = Finset<int>.Of(2, 3, 4);
        var i = a.Inter(b);
        Assert.Equal(2, i.Card);
        Assert.True(i.Contains(2));
        Assert.True(i.Contains(3));
    }

    [Fact]
    public void SDiff()
    {
        var a = Finset<int>.Of(1, 2, 3);
        var b = Finset<int>.Of(2, 3, 4);
        var d = a.SDiff(b);
        Assert.Equal(1, d.Card);
        Assert.True(d.Contains(1));
    }

    [Fact]
    public void SetIdentities()
    {
        var a = Finset<int>.Of(1, 2, 3);
        Assert.Equal(a, a.Union(a));
        Assert.Equal(a, a.Inter(a));
        Assert.Equal(Finset<int>.Empty, a.SDiff(a));
        Assert.Equal(a, a.Union(Finset<int>.Empty));
        Assert.Equal(Finset<int>.Empty, a.Inter(Finset<int>.Empty));
    }

    // --- Powerset ---

    [Fact]
    public void PowersetOfTwoElements()
    {
        var s = Finset<int>.Of(1, 2);
        var ps = s.Powerset().ToList();
        Assert.Equal(4, ps.Count); // 2^2
        Assert.Contains(Finset<int>.Empty, ps);
        Assert.Contains(Finset<int>.Of(1), ps);
        Assert.Contains(Finset<int>.Of(2), ps);
        Assert.Contains(Finset<int>.Of(1, 2), ps);
    }

    [Fact]
    public void PowersetCardCount()
    {
        var s = Finset<int>.Of(1, 2, 3);
        Assert.Equal(8, s.Powerset().Count()); // 2^3
    }

    [Fact]
    public void PowersetCardK()
    {
        var s = Finset<int>.Of(1, 2, 3, 4);
        Assert.Single(s.PowersetCard(0));                // C(4,0) = 1
        Assert.Equal(4, s.PowersetCard(1).Count());      // C(4,1) = 4
        Assert.Equal(6, s.PowersetCard(2).Count());      // C(4,2) = 6
        Assert.Equal(4, s.PowersetCard(3).Count());      // C(4,3) = 4
        Assert.Single(s.PowersetCard(4));                // C(4,4) = 1
    }

    // --- Sum and Prod ---

    [Fact]
    public void SumOverElements()
    {
        var s = Finset<int>.Of(1, 2, 3);
        var result = s.Sum<Integer>(x => (Integer)x);
        Assert.Equal((Integer)6, result);
    }

    [Fact]
    public void ProdOverElements()
    {
        var s = Finset<int>.Of(1, 2, 3);
        var result = s.Prod<Integer>(x => (Integer)x);
        Assert.Equal((Integer)6, result);
    }

    [Fact]
    public void SumOverEmptyIsZero()
    {
        var s = Finset<int>.Empty;
        Assert.Equal(Integer.Zero, s.Sum<Integer>(x => (Integer)x));
    }

    [Fact]
    public void ProdOverEmptyIsOne()
    {
        var s = Finset<int>.Empty;
        Assert.Equal(Integer.One, s.Prod<Integer>(x => (Integer)x));
    }

    // --- Image and Filter ---

    [Fact]
    public void ImageCollapsingReducesCard()
    {
        var s = Finset<int>.Of(1, 2, 3, 4);
        var img = s.Image(x => x % 2); // {0, 1}
        Assert.Equal(2, img.Card);
    }

    [Fact]
    public void FilterKeepsMatching()
    {
        var s = Finset<int>.Of(1, 2, 3, 4, 5);
        var even = s.Filter(x => x % 2 == 0);
        Assert.Equal(2, even.Card);
        Assert.True(even.Contains(2));
        Assert.True(even.Contains(4));
    }

    // --- Equality ---

    [Fact]
    public void EqualSets()
    {
        var a = Finset<int>.Of(3, 1, 2);
        var b = Finset<int>.Of(1, 2, 3);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSets()
    {
        var a = Finset<int>.Of(1, 2);
        var b = Finset<int>.Of(1, 3);
        Assert.NotEqual(a, b);
    }
}
