namespace Helium.Primitives.Tests;

public class NumberTheoryTests
{
    [Fact]
    public void Divisors_ReturnsPositiveDivisorsInOrder()
    {
        Assert.Equal(
            FiniteList<Integer>.Of(1, 2, 3, 4, 6, 12),
            IntegerNumberTheory.Divisors(12));
    }

    [Fact]
    public void Divisors_OfZeroIsEmpty()
    {
        Assert.Equal(FiniteList<Integer>.Empty, IntegerNumberTheory.Divisors(0));
    }

    [Fact]
    public void TrialDivisionFactor_FactorsAbsoluteValue()
    {
        var factors = IntegerNumberTheory.TrialDivisionFactor(-60);

        Assert.Equal(3, factors.Length);
        Assert.Equal(new Pair<Integer, Nat>(2, 2), factors[0]);
        Assert.Equal(new Pair<Integer, Nat>(3, 1), factors[1]);
        Assert.Equal(new Pair<Integer, Nat>(5, 1), factors[2]);
    }

    [Fact]
    public void PowMod_ComputesModularPower()
    {
        Assert.Equal((Integer)4, IntegerNumberTheory.PowMod(2, 10, 17));
    }

    [Fact]
    public void PowMod_RejectsNegativeExponent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerNumberTheory.PowMod(2, -1, 5));
    }

    [Fact]
    public void ModInverse_ReturnsInverseWhenItExists()
    {
        Assert.Equal((Integer)4, IntegerNumberTheory.ModInverse(3, 11));
        Assert.Null(IntegerNumberTheory.ModInverse(2, 4));
    }

    [Fact]
    public void Totient_ComputesEulerPhi()
    {
        Assert.Equal((Integer)1, IntegerNumberTheory.Totient(1));
        Assert.Equal((Integer)4, IntegerNumberTheory.Totient(10));
        Assert.Equal((Integer)12, IntegerNumberTheory.Totient(36));
    }

    [Fact]
    public void DivisorSigma_ComputesSumOfPowers()
    {
        Assert.Equal((Integer)28, IntegerNumberTheory.DivisorSigma(12));
        Assert.Equal((Integer)210, IntegerNumberTheory.DivisorSigma(12, power: 2));
    }

    [Fact]
    public void IntNumberTheory_DelegatesToExactHelpers()
    {
        Assert.Equal(4, IntNumberTheory.PowMod(2, 10, 17));
        Assert.Equal(4, IntNumberTheory.ModInverse(3, 11));
    }
}
