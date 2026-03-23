using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class MultisetTests
{
    // --- Construction ---

    [Fact]
    public void EmptyMultiset()
    {
        var m = Multiset<int>.Empty;
        Assert.True(m.IsEmpty);
        Assert.Equal(0, m.Card);
    }

    [Fact]
    public void FromElementsWithDuplicates()
    {
        var m = Multiset<int>.FromElements([1, 1, 2]);
        Assert.Equal(2, m.Count(1));
        Assert.Equal(1, m.Count(2));
        Assert.Equal(3, m.Card);
    }

    // --- Count and Card ---

    [Fact]
    public void CountAbsentElementReturnsZero()
    {
        var m = Multiset<int>.FromElements([1, 2]);
        Assert.Equal(0, m.Count(99));
    }

    [Fact]
    public void CardIsSumOfCounts()
    {
        var m = Multiset<int>.FromElements([1, 1, 1, 2, 2]);
        Assert.Equal(5, m.Card);
    }

    // --- Add and Remove ---

    [Fact]
    public void AddIncrementsCount()
    {
        var m = Multiset<int>.FromElements([1, 2]);
        var m2 = m.Add(1);
        Assert.Equal(2, m2.Count(1));
        Assert.Equal(1, m2.Count(2));
    }

    [Fact]
    public void RemoveDecrementsCount()
    {
        var m = Multiset<int>.FromElements([1, 1, 2]);
        var m2 = m.Remove(1);
        Assert.Equal(1, m2.Count(1));
    }

    [Fact]
    public void RemoveLastOccurrenceRemovesElement()
    {
        var m = Multiset<int>.FromElements([1, 2]);
        var m2 = m.Remove(1);
        Assert.Equal(0, m2.Count(1));
    }

    [Fact]
    public void RemoveAbsentElementNoChange()
    {
        var m = Multiset<int>.FromElements([1, 2]);
        var m2 = m.Remove(99);
        Assert.Equal(m, m2);
    }

    // --- Union, Inter, Sum ---

    [Fact]
    public void UnionIsMaxOfCounts()
    {
        var a = Multiset<int>.FromElements([1, 1, 2]);
        var b = Multiset<int>.FromElements([1, 2, 2, 3]);
        var u = a.Union(b);
        Assert.Equal(2, u.Count(1)); // max(2,1)
        Assert.Equal(2, u.Count(2)); // max(1,2)
        Assert.Equal(1, u.Count(3)); // max(0,1)
    }

    [Fact]
    public void InterIsMinOfCounts()
    {
        var a = Multiset<int>.FromElements([1, 1, 2]);
        var b = Multiset<int>.FromElements([1, 2, 2, 3]);
        var i = a.Inter(b);
        Assert.Equal(1, i.Count(1)); // min(2,1)
        Assert.Equal(1, i.Count(2)); // min(1,2)
        Assert.Equal(0, i.Count(3)); // min(0,1)
    }

    [Fact]
    public void SumIsSumOfCounts()
    {
        var a = Multiset<int>.FromElements([1, 1, 2]);
        var b = Multiset<int>.FromElements([1, 2, 3]);
        var s = a.Sum(b);
        Assert.Equal(3, s.Count(1)); // 2+1
        Assert.Equal(2, s.Count(2)); // 1+1
        Assert.Equal(1, s.Count(3)); // 0+1
        Assert.Equal(6, s.Card);
    }

    // --- Map and Filter ---

    [Fact]
    public void MapCollapsingElementsSumsCounts()
    {
        var m = Multiset<int>.FromElements([1, 2, 3]);
        // Map all to the same value.
        var mapped = m.Map(_ => 0);
        Assert.Equal(3, mapped.Count(0));
    }

    [Fact]
    public void FilterKeepsSatisfyingElements()
    {
        var m = Multiset<int>.FromElements([1, 2, 3, 4, 5]);
        var even = m.Filter(x => x % 2 == 0);
        Assert.Equal(1, even.Count(2));
        Assert.Equal(1, even.Count(4));
        Assert.Equal(0, even.Count(1));
        Assert.Equal(2, even.Card);
    }

    // --- ToFinset ---

    [Fact]
    public void ToFinsetDropsMultiplicity()
    {
        var m = Multiset<int>.FromElements([1, 1, 2]);
        var fs = m.ToFinset();
        Assert.Equal(2, fs.Card);
        Assert.True(fs.Contains(1));
        Assert.True(fs.Contains(2));
    }

    // --- Equality ---

    [Fact]
    public void EqualMultisets()
    {
        var a = Multiset<int>.FromElements([1, 2, 1]);
        var b = Multiset<int>.FromElements([1, 1, 2]);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentMultiplicitiesNotEqual()
    {
        var a = Multiset<int>.FromElements([1, 2]);
        var b = Multiset<int>.FromElements([1, 1, 2]);
        Assert.NotEqual(a, b);
    }
}
