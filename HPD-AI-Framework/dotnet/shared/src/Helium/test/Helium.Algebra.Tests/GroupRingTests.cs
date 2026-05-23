using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class GroupRingTests
{
    [Fact]
    public void Addition_CombinesCoefficients()
    {
        var a = GroupRing<Integer, C3>.Term(new C3(1), (Integer)2);
        var b = GroupRing<Integer, C3>.Term(new C3(1), (Integer)3);

        Assert.Equal((Integer)5, (a + b).Coefficient(new C3(1)));
    }

    [Fact]
    public void Multiplication_ConvolvesByGroupMultiplication()
    {
        var a = GroupRing<Integer, C3>.Term(new C3(1), (Integer)2);
        var b = GroupRing<Integer, C3>.Term(new C3(2), (Integer)3);

        var product = a * b;

        Assert.Equal((Integer)6, product.Coefficient(C3.Identity));
    }

    [Fact]
    public void Unit_IsIdentityForMultiplication()
    {
        var x =
            GroupRing<Integer, C3>.Term(new C3(1), (Integer)2) +
            GroupRing<Integer, C3>.Term(new C3(2), (Integer)5);

        var one = GroupRing<Integer, C3>.Unit(Integer.One);

        Assert.Equal(x, one * x);
        Assert.Equal(x, x * one);
    }

    [Fact]
    public void Comultiplication_OnBasisElement_IsBasisTensorItself()
    {
        var basis = GroupRing<Integer, C3>.Basis(new C3(2));
        var delta = GroupRing<Integer, C3>.Comultiplication(basis);

        Assert.Equal(Integer.One, delta.Coefficient(basis, basis));
    }

    [Fact]
    public void Counit_SendsEveryBasisElementToOne()
    {
        Assert.Equal(Integer.One, GroupRing<Integer, C3>.Counit(GroupRing<Integer, C3>.Basis(C3.Identity)));
        Assert.Equal(Integer.One, GroupRing<Integer, C3>.Counit(GroupRing<Integer, C3>.Basis(new C3(1))));
        Assert.Equal(Integer.One, GroupRing<Integer, C3>.Counit(GroupRing<Integer, C3>.Basis(new C3(2))));
    }

    [Fact]
    public void Antipode_InvertsBasisElements()
    {
        var basis = GroupRing<Integer, C3>.Basis(new C3(1));
        var antipode = GroupRing<Integer, C3>.Antipode(basis);

        Assert.Equal(GroupRing<Integer, C3>.Basis(new C3(2)), antipode);
    }

    [Fact]
    public void Coassociativity_HoldsOnBasisComponents()
    {
        var basis = GroupRing<Integer, C3>.Basis(new C3(1));
        var delta = GroupRing<Integer, C3>.Comultiplication(basis);
        var component = Assert.Single(delta.Components);

        var leftNested = GroupRing<Integer, C3>.Comultiplication(component.Left);
        var rightNested = GroupRing<Integer, C3>.Comultiplication(component.Right);

        Assert.Equal(Integer.One, leftNested.Coefficient(basis, basis));
        Assert.Equal(Integer.One, rightNested.Coefficient(basis, basis));
        Assert.Equal(component.Left, component.Right);
    }

    [Fact]
    public void HopfAntipodeLaw_HoldsOnBasisElement()
    {
        var basis = GroupRing<Integer, C3>.Basis(new C3(1));
        var delta = GroupRing<Integer, C3>.Comultiplication(basis);

        var left = GroupRing<Integer, C3>.Zero;
        foreach (var (x, y, coefficient) in delta.Components)
            left += coefficient * (GroupRing<Integer, C3>.Antipode(x) * y);

        Assert.Equal(GroupRing<Integer, C3>.Unit(GroupRing<Integer, C3>.Counit(basis)), left);
    }

    private readonly struct C3 :
        IGroup<C3>,
        ITotalOrder<C3>,
        IEquatable<C3>
    {
        public int Value { get; }

        public C3(int value)
        {
            Value = ((value % 3) + 3) % 3;
        }

        public static C3 Identity => new(0);

        public static C3 Multiply(C3 left, C3 right) => new(left.Value + right.Value);

        public static C3 Invert(C3 value) => new(-value.Value);

        public static bool DecidableEquals(C3 left, C3 right) => left == right;

        public static bool LessEqual(C3 left, C3 right) => CompareOrder(left, right) != Ordering.Greater;

        public static Ordering CompareOrder(C3 left, C3 right)
        {
            if (left.Value < right.Value) return Ordering.Less;
            if (left.Value > right.Value) return Ordering.Greater;
            return Ordering.Equal;
        }

        public bool Equals(C3 other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is C3 other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(C3 left, C3 right) => left.Equals(right);
        public static bool operator !=(C3 left, C3 right) => !left.Equals(right);
        public override string ToString() => $"c{Value}";
    }
}
