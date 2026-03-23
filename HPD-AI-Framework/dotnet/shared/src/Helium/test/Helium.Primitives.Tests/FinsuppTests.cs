using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class FinsuppTests
{
    // --- Construction ---

    [Fact]
    public void EmptyIsZero()
    {
        var f = Finsupp<int, Integer>.Empty;
        Assert.True(f.IsZero);
        Assert.Equal(0, f.Count);
    }

    [Fact]
    public void SingleCreatesOneEntry()
    {
        var f = Finsupp<int, Integer>.Single(3, (Integer)5);
        Assert.False(f.IsZero);
        Assert.Equal(1, f.Count);
        Assert.Equal((Integer)5, f[3]);
    }

    [Fact]
    public void SingleWithZeroIsEmpty()
    {
        var f = Finsupp<int, Integer>.Single(3, Integer.Zero);
        Assert.True(f.IsZero);
        Assert.Equal(0, f.Count);
    }

    [Fact]
    public void FromDictionaryStripsZeros()
    {
        var f = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 0, (Integer)1 },
            { 1, Integer.Zero },
            { 2, (Integer)3 },
        });
        Assert.Equal(2, f.Count);
        Assert.Equal((Integer)1, f[0]);
        Assert.Equal(Integer.Zero, f[1]);
        Assert.Equal((Integer)3, f[2]);
    }

    // --- Lookup ---

    [Fact]
    public void LookupAbsentKeyReturnsZero()
    {
        var f = Finsupp<int, Integer>.Single(1, (Integer)42);
        Assert.Equal(Integer.Zero, f[99]);
    }

    // --- Zero invariant (THE critical invariant) ---

    [Fact]
    public void ZipWithStripsZeros()
    {
        var a = Finsupp<int, Integer>.Single(1, (Integer)5);
        var b = Finsupp<int, Integer>.Single(1, (Integer)(-5));
        var result = Finsupp<int, Integer>.ZipWith((x, y) => x + y, a, b);
        Assert.True(result.IsZero);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void SubtractSelfGivesEmpty()
    {
        var a = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 0, (Integer)3 },
            { 1, (Integer)(-7) },
            { 5, (Integer)100 },
        });
        var result = a - a;
        Assert.True(result.IsZero);
    }

    [Fact]
    public void SetToZeroRemovesKey()
    {
        var f = Finsupp<int, Integer>.Single(1, (Integer)42);
        var g = f.Set(1, Integer.Zero);
        Assert.True(g.IsZero);
    }

    [Fact]
    public void MapRangeStripsZeros()
    {
        var a = Finsupp<int, Integer>.Single(1, (Integer)5);
        var result = Finsupp<int, Integer>.MapRange(x => x * Integer.Zero, a);
        Assert.True(result.IsZero);
    }

    // --- Core operations ---

    [Fact]
    public void ZipWithPointwiseAdd()
    {
        var a = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 0, (Integer)1 },
            { 1, (Integer)2 },
        });
        var b = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 1, (Integer)3 },
            { 2, (Integer)4 },
        });
        var result = a + b;

        Assert.Equal((Integer)1, result[0]);
        Assert.Equal((Integer)5, result[1]);
        Assert.Equal((Integer)4, result[2]);
    }

    [Fact]
    public void NegateAll()
    {
        var a = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 0, (Integer)3 },
            { 1, (Integer)(-7) },
        });
        var neg = -a;
        Assert.Equal((Integer)(-3), neg[0]);
        Assert.Equal((Integer)7, neg[1]);
    }

    [Fact]
    public void ScalarMultiply()
    {
        var a = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 0, (Integer)2 },
            { 1, (Integer)3 },
        });
        var result = a.ScalarMultiply((Integer)4);
        Assert.Equal((Integer)8, result[0]);
        Assert.Equal((Integer)12, result[1]);
    }

    [Fact]
    public void ScalarMultiplyByZeroGivesEmpty()
    {
        var a = Finsupp<int, Integer>.Single(1, (Integer)42);
        var result = a.ScalarMultiply(Integer.Zero);
        Assert.True(result.IsZero);
    }

    // --- Equality ---

    [Fact]
    public void EqualFinsuppsAreEqual()
    {
        var a = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 1, (Integer)2 },
            { 3, (Integer)4 },
        });
        var b = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 3, (Integer)4 },
            { 1, (Integer)2 },
        });
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentFinsuppsAreNotEqual()
    {
        var a = Finsupp<int, Integer>.Single(1, (Integer)2);
        var b = Finsupp<int, Integer>.Single(1, (Integer)3);
        Assert.NotEqual(a, b);
    }

    // --- Support ---

    [Fact]
    public void SupportIsExactlyNonzeroKeys()
    {
        var f = Finsupp<int, Integer>.FromDictionary(new Dictionary<int, Integer>
        {
            { 0, (Integer)1 },
            { 1, Integer.Zero },
            { 2, (Integer)3 },
        });
        var support = f.Support.ToHashSet();
        Assert.Contains(0, support);
        Assert.DoesNotContain(1, support);
        Assert.Contains(2, support);
    }
}
