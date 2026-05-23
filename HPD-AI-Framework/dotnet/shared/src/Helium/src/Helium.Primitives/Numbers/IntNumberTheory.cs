namespace Helium.Primitives;

public static class IntNumberTheory
{
    public static FiniteList<Integer> Divisors(int n) =>
        IntegerNumberTheory.Divisors((Integer)n);

    public static FiniteList<Pair<Integer, Nat>> TrialDivisionFactor(int n) =>
        IntegerNumberTheory.TrialDivisionFactor((Integer)n);

    public static int PowMod(int value, int exponent, int modulus)
    {
        var result = IntegerNumberTheory.PowMod((Integer)value, (Integer)exponent, (Integer)modulus);
        return (int)(System.Numerics.BigInteger)result;
    }

    public static int? ModInverse(int value, int modulus)
    {
        var result = IntegerNumberTheory.ModInverse((Integer)value, (Integer)modulus);
        return result is null ? null : (int)(System.Numerics.BigInteger)result.Value;
    }
}
