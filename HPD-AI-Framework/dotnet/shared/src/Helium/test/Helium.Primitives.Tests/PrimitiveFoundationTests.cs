using Helium.Primitives.Tests.Axioms;

namespace Helium.Primitives.Tests;

public class PrimitiveFoundationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Nat_AcceptsNonnegativeValues(int value)
    {
        var n = new Nat(value);
        Assert.Equal(value, n.Value);
        OrderedAxioms.VerifyTotality(n, new Nat(0));
    }

    [Fact]
    public void Nat_RejectsNegativeValues() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Nat(-1));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Degree_AcceptsNonnegativeValues(int value)
    {
        var degree = new Degree(value);
        Assert.Equal(value, degree.Value);
        OrderedAxioms.VerifyTotality(degree, new Degree(0));
    }

    [Fact]
    public void Degree_RejectsNegativeValues() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Degree(-1));

    [Fact]
    public void Fin_RequiresValueInsideBound()
    {
        var value = new Fin(2, 5);
        Assert.Equal(2, value.Value);
        Assert.Equal(5, value.Bound);
        OrderedAxioms.VerifyTotality(value, new Fin(4, 5));
    }

    [Fact]
    public void Fin_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fin(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fin(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fin(0, 0));
    }

    [Fact]
    public void Integer_UsesHeliumTotalOrder()
    {
        Assert.True(Integer.LessEqual((Integer)1, (Integer)2));
        Assert.Equal(Ordering.Less, Integer.CompareOrder((Integer)1, (Integer)2));
        Assert.True(Integer.DecidableEquals((Integer)3, (Integer)3));
    }

    [Fact]
    public void Rational_UsesHeliumTotalOrder()
    {
        var third = Rational.Create((Integer)1, (Integer)3);
        var half = Rational.Create((Integer)1, (Integer)2);

        Assert.True(Rational.LessEqual(third, half));
        Assert.Equal(Ordering.Less, Rational.CompareOrder(third, half));
        Assert.True(Rational.DecidableEquals(half, Rational.Create((Integer)2, (Integer)4)));
    }

    [Fact]
    public void OldIOrderedContract_IsNotPublic()
    {
        var names = typeof(Integer).Assembly.GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("IOrdered`1", names);
        Assert.Contains("ITotalOrder`1", names);
    }
}
