namespace Helium.Primitives.Tests;

public class OrderTheoryTests
{
    [Fact]
    public void BoolAlgebra_HasExpectedBoundsAndEnumeration()
    {
        Assert.Equal(BoolAlgebra.Top, BoolAlgebra.Join(false, true));
        Assert.Equal(BoolAlgebra.Bottom, BoolAlgebra.Meet(false, true));
        Assert.Equal(2, BoolAlgebra.Cardinality);
        Assert.Equal(FiniteList<BoolAlgebra>.Of(false, true), BoolAlgebra.Elements);
    }

    [Fact]
    public void BoolAlgebra_ComplementLaws()
    {
        foreach (var x in BoolAlgebra.Elements)
        {
            Assert.Equal(BoolAlgebra.Top, BoolAlgebra.Join(x, BoolAlgebra.Complement(x)));
            Assert.Equal(BoolAlgebra.Bottom, BoolAlgebra.Meet(x, BoolAlgebra.Complement(x)));
        }
    }

    [Fact]
    public void BoolAlgebra_DeMorganLaws()
    {
        foreach (var x in BoolAlgebra.Elements)
        foreach (var y in BoolAlgebra.Elements)
        {
            Assert.Equal(
                BoolAlgebra.Complement(BoolAlgebra.Join(x, y)),
                BoolAlgebra.Meet(BoolAlgebra.Complement(x), BoolAlgebra.Complement(y)));

            Assert.Equal(
                BoolAlgebra.Complement(BoolAlgebra.Meet(x, y)),
                BoolAlgebra.Join(BoolAlgebra.Complement(x), BoolAlgebra.Complement(y)));
        }
    }

    [Fact]
    public void BoolAlgebra_FiniteSupremumAndInfimum()
    {
        Assert.Equal(BoolAlgebra.Top, BoolAlgebra.Supremum(FiniteList<BoolAlgebra>.Of(false, false, true)));
        Assert.Equal(BoolAlgebra.Bottom, BoolAlgebra.Infimum(FiniteList<BoolAlgebra>.Of(true, true, false)));
        Assert.Equal(BoolAlgebra.Bottom, BoolAlgebra.Supremum(FiniteList<BoolAlgebra>.Empty));
        Assert.Equal(BoolAlgebra.Top, BoolAlgebra.Infimum(FiniteList<BoolAlgebra>.Empty));
    }

    [Fact]
    public void FinitePowerSet_JoinMeetComplement()
    {
        var universe = Finset<Integer>.Of(1, 2, 3);
        var a = new FinitePowerSet<Integer>(universe, Finset<Integer>.Of(1, 2));
        var b = new FinitePowerSet<Integer>(universe, Finset<Integer>.Of(2, 3));

        Assert.Equal(Finset<Integer>.Of(1, 2, 3), a.Join(b).Elements);
        Assert.Equal(Finset<Integer>.Of(2), a.Meet(b).Elements);
        Assert.Equal(Finset<Integer>.Of(3), a.Complement().Elements);
        Assert.True(a.Meet(b).LessEqual(a));
    }

    [Fact]
    public void FinitePowerSet_RejectsElementsOutsideUniverse()
    {
        var universe = Finset<Integer>.Of(1, 2);
        Assert.Throws<ArgumentException>(() =>
            new FinitePowerSet<Integer>(universe, Finset<Integer>.Of(1, 3)));
    }

    [Fact]
    public void OrderHomomorphism_Composes()
    {
        var not = new OrderHomomorphism<BoolAlgebra, BoolAlgebra>(BoolAlgebra.Complement);
        var twice = OrderHomomorphism<BoolAlgebra, BoolAlgebra>.Compose(not, not);

        Assert.Equal(BoolAlgebra.Top, twice.Apply(BoolAlgebra.Top));
        Assert.Equal(BoolAlgebra.Bottom, twice.Apply(BoolAlgebra.Bottom));
    }
}
