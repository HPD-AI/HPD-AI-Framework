using Helium.Primitives.Tests.Axioms;

namespace Helium.Primitives.Tests;

public class ExactStructureInterfaceTests
{
    [Fact]
    public void Integer_IsAdditiveCommutativeGroup()
    {
        AddGroupAxioms.VerifyAll((Integer)2, (Integer)(-3), (Integer)5);
    }

    [Fact]
    public void Integer_IsModuleOverItself()
    {
        ModuleAxioms.VerifyAll<Integer, Integer>((Integer)2, (Integer)3, (Integer)4, (Integer)(-5));
    }

    [Fact]
    public void Rational_IsDivisionRingThroughField()
    {
        DivisionRingAxioms.VerifyAll(
            Rational.Create((Integer)2, (Integer)3),
            Rational.Create((Integer)5, (Integer)7),
            Rational.Create((Integer)(-11), (Integer)13));
    }

    [Fact]
    public void Rational_IsModuleOverItself()
    {
        ModuleAxioms.VerifyAll<Rational, Rational>(
            Rational.Create((Integer)2, (Integer)3),
            Rational.Create((Integer)5, (Integer)7),
            Rational.Create((Integer)11, (Integer)13),
            Rational.Create((Integer)(-17), (Integer)19));
    }

    [Fact]
    public void CyclicGroup3_SatisfiesGroupLaws()
    {
        GroupAxioms.VerifyAll(new CyclicGroup3(1), new CyclicGroup3(2), CyclicGroup3.Identity);
    }

    [Fact]
    public void PrimeModulusWitness_ExposesExactPrimeValue()
    {
        Assert.Equal((Integer)5, Prime5.Value);
    }

    private readonly struct CyclicGroup3 : IEquatable<CyclicGroup3>, IGroup<CyclicGroup3>
    {
        public int Value { get; }

        public CyclicGroup3(int value)
        {
            Value = ((value % 3) + 3) % 3;
        }

        public static CyclicGroup3 Identity => new(0);

        public static CyclicGroup3 Multiply(CyclicGroup3 left, CyclicGroup3 right) =>
            new(left.Value + right.Value);

        public static CyclicGroup3 Invert(CyclicGroup3 value) => new(-value.Value);

        public static bool DecidableEquals(CyclicGroup3 left, CyclicGroup3 right) => left == right;

        public bool Equals(CyclicGroup3 other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is CyclicGroup3 other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(CyclicGroup3 left, CyclicGroup3 right) => left.Equals(right);
        public static bool operator !=(CyclicGroup3 left, CyclicGroup3 right) => !left.Equals(right);
    }

    private readonly struct Prime5 : IPrimeModulus
    {
        public static Integer Value => (Integer)5;
    }
}
