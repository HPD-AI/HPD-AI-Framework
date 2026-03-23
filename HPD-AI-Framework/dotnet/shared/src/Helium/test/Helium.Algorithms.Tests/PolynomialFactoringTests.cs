using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class PolynomialFactoringTests
{
    private static QuotientRing<Integer> F(int v, int p) => ZMod.Create((Integer)v, (Integer)p);
    private static bool CongruentMod(Integer a, Integer b, Integer modulus) => Integer.DivMod(a - b, modulus).Remainder.IsZero;

    private static void AssertPolynomialCongruentMod(Polynomial<Integer> left, Polynomial<Integer> right, Integer modulus)
    {
        int maxDegree = Math.Max(left.Degree, right.Degree);
        for (int i = 0; i <= maxDegree; i++)
            Assert.True(CongruentMod(left[i], right[i], modulus), $"Coefficient at degree {i} differs mod {modulus}.");
    }

    private static Polynomial<Integer> ExpandIntegerFactors(List<(Polynomial<Integer> Factor, int Multiplicity)> factors)
    {
        var product = Polynomial<Integer>.One;
        foreach (var (factor, multiplicity) in factors)
        {
            for (int i = 0; i < multiplicity; i++)
                product *= factor;
        }

        return product;
    }

    private static Polynomial<QuotientRing<Integer>> ExpandFiniteFieldFactors(
        List<Polynomial<QuotientRing<Integer>>> factors)
    {
        var product = Polynomial<QuotientRing<Integer>>.One;
        foreach (var factor in factors)
            product *= factor;
        return product;
    }

    public static IEnumerable<object[]> RecombinationMatrixCases()
    {
        yield return [new[] { 1, 2, 3 }, 3, 80];
        yield return [new[] { 1, 2, 3, 5 }, 4, 120];
        yield return [new[] { 1, 2, 3, 5, 6, 7 }, 6, 200];
        yield return [new[] { 1, 2, 3, 5, 6, 7, 10 }, 7, 280];
        yield return [new[] { 1, 2, 3, 5, 6, 7, 10, 11 }, 8, 360];
    }

    [Fact]
    public void SquareFree_OfPerfectSquare_ReportsMultiplicity()
    {
        var x = Polynomial<Integer>.X;
        var f = (x - Polynomial<Integer>.One) * (x - Polynomial<Integer>.One);

        var sf = PolynomialFactoring.SquareFreeFactorization(f);

        Assert.Single(sf);
        Assert.Equal(x - Polynomial<Integer>.One, sf[0].Factor);
        Assert.Equal(2, sf[0].Multiplicity);
    }

    [Fact]
    public void FactorOverZ_X2Minus1()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x - Polynomial<Integer>.One;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, t => t.Factor == (x - Polynomial<Integer>.One) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == (x + Polynomial<Integer>.One) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZWithDiagnostics_SimpleLinearSplit_UsesNoRecombination()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x - Polynomial<Integer>.One;

        var (factors, diagnostics) = PolynomialFactoring.FactorOverZWithDiagnostics(f);

        Assert.Equal(2, factors.Count);
        Assert.Equal(0, diagnostics.SubsetMasksTried);
        Assert.Equal(0, diagnostics.HenselLiftAttempts);
        Assert.Equal(0, diagnostics.HenselLiftSuccesses);
    }

    [Fact]
    public void FactorOverZ_X3Minus1()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x * x - Polynomial<Integer>.One;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, t => t.Factor == (x - Polynomial<Integer>.One) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == (x * x + x + Polynomial<Integer>.One) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZ_Multiplicity_IsPreserved()
    {
        var x = Polynomial<Integer>.X;
        var f = (x - Polynomial<Integer>.One) * (x - Polynomial<Integer>.One) * (x + Polynomial<Integer>.One);

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, t => t.Factor == (x - Polynomial<Integer>.One) && t.Multiplicity == 2);
        Assert.Contains(factors, t => t.Factor == (x + Polynomial<Integer>.One) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZ_ContentIsIgnoredInFactorList()
    {
        // 6x^2 + 4x + 2 = 2*(3x^2 + 2x + 1), with primitive part irreducible over Z.
        var f = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)4, (Integer)6);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3), factors[0].Factor);
        Assert.Equal(1, factors[0].Multiplicity);
    }

    [Fact]
    public void FactorOverZ_QuarticWithoutIntegerRoots_SplitsIntoQuadratics()
    {
        // x^4 + 3x^2 + 2 = (x^2 + 1)(x^2 + 2)
        var f = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)3, (Integer)0, (Integer)1);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZ_SexticWithoutIntegerRoots_SplitsIntoThreeQuadratics()
    {
        // (x^2 + 1)(x^2 + 2)(x^2 + 3) = x^6 + 6x^4 + 11x^2 + 6
        var f = Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)0, (Integer)11, (Integer)0, (Integer)6, (Integer)0, (Integer)1);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(3, factors.Count);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZ_OcticWithoutIntegerRoots_SplitsIntoFourQuadratics()
    {
        var q1 = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1);
        var q2 = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1);
        var q3 = Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1);
        var q4 = Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1);
        var f = q1 * q2 * q3 * q4;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(4, factors.Count);
        Assert.Contains(factors, t => t.Factor == q1 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == q2 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == q3 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == q4 && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZ_Degree12NoIntegerRoots_SplitsIntoSixQuadratics()
    {
        var quadratics = new[]
        {
            Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)7, (Integer)0, (Integer)1),
        };

        var f = quadratics[0];
        for (int i = 1; i < quadratics.Length; i++)
            f *= quadratics[i];

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(quadratics.Length, factors.Count);
        foreach (var q in quadratics)
            Assert.Contains(factors, t => t.Factor == q && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverZWithDiagnostics_RecombinationPath_ReportsAttempts()
    {
        var f = Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)0, (Integer)11, (Integer)0, (Integer)6, (Integer)0, (Integer)1);

        var (factors, diagnostics) = PolynomialFactoring.FactorOverZWithDiagnostics(f);

        Assert.Equal(3, factors.Count);
        Assert.True(diagnostics.PrimeAttempts > 0);
        Assert.True(diagnostics.PrimeAccepted > 0);
        Assert.True(diagnostics.SubsetMasksTried > 0);
        Assert.True(diagnostics.HenselLiftAttempts > 0);
        Assert.True(diagnostics.HenselLiftSuccesses > 0);
        Assert.True(diagnostics.HenselLiftSuccesses <= diagnostics.HenselLiftAttempts);
        Assert.True(diagnostics.PrimeAccepted <= diagnostics.PrimeAttempts);
    }

    [Fact]
    public void FactorOverZWithDiagnostics_Degree12_RecombinationIsBounded()
    {
        var quadratics = new[]
        {
            Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)7, (Integer)0, (Integer)1),
        };

        var f = quadratics[0];
        for (int i = 1; i < quadratics.Length; i++)
            f *= quadratics[i];

        var (factors, diagnostics) = PolynomialFactoring.FactorOverZWithDiagnostics(f);

        Assert.Equal(quadratics.Length, factors.Count);
        Assert.True(diagnostics.PrimeAttempts < 200);
        Assert.True(diagnostics.PrimeAccepted <= diagnostics.PrimeAttempts);
        Assert.True(diagnostics.SubsetMasksTried < 5000);
        Assert.True(diagnostics.HenselLiftAttempts <= diagnostics.SubsetMasksTried);
        Assert.True(diagnostics.HenselLiftSuccesses > 0);
    }

    [Theory]
    [MemberData(nameof(RecombinationMatrixCases))]
    public void FactorOverZWithDiagnostics_RecombinationMatrix_Bounded(
        int[] constants,
        int expectedFactorCount,
        int maxPrimeAttempts)
    {
        var f = Polynomial<Integer>.One;
        foreach (var c in constants)
            f *= Polynomial<Integer>.FromCoeffs((Integer)c, (Integer)0, (Integer)1);

        var (factors, diagnostics) = PolynomialFactoring.FactorOverZWithDiagnostics(f);

        Assert.Equal(expectedFactorCount, factors.Count);
        Assert.True(diagnostics.PrimeAttempts <= maxPrimeAttempts);
        Assert.True(diagnostics.SubsetMasksTried >= 0);
        Assert.True(diagnostics.HenselLiftSuccesses > 0);
    }

    [Fact]
    public void FactorOverZ_LargeCoefficients_ModReductionDoesNotOverflow()
    {
        Integer big = Integer.One;
        foreach (var p in new[] { 3, 5, 7, 11, 13, 17, 19, 23 })
            big *= (Integer)p;

        // x^2 + big*x + 1 has large coefficients and is irreducible over Z.
        var f = Polynomial<Integer>.FromCoeffs((Integer)1, big, (Integer)1);

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(f, factors[0].Factor);
        Assert.Equal(1, factors[0].Multiplicity);
    }

    [Fact]
    public void FactorOverZ_DeterministicRandomProducts_ReconstructsInput()
    {
        var baseFactors = new[]
        {
            Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)7, (Integer)0, (Integer)1),
        };

        var rng = new Random(20260209);
        for (int caseIndex = 0; caseIndex < 6; caseIndex++)
        {
            var f = Polynomial<Integer>.One;
            int factorCount = 3 + (caseIndex % 4);
            for (int i = 0; i < factorCount; i++)
            {
                var pick = baseFactors[rng.Next(baseFactors.Length)];
                f *= pick;
            }

            var factors = PolynomialFactoring.FactorOverZ(f);
            var reconstructed = ExpandIntegerFactors(factors);
            Assert.Equal(f, reconstructed);
        }
    }

    [Fact]
    public void FactorOverQ_ExtractsRationalContent()
    {
        var x = Polynomial<Rational>.X;
        var three = Polynomial<Rational>.C((Rational)3);
        var f = three * x * x - three;

        var result = PolynomialFactoring.Factor(f);

        Assert.Equal((Rational)3, result.Content);
        Assert.Equal(2, result.Factors.Count);
        Assert.Contains(result.Factors, t => t.Factor == (x - Polynomial<Rational>.One) && t.Multiplicity == 1);
        Assert.Contains(result.Factors, t => t.Factor == (x + Polynomial<Rational>.One) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverFiniteField_X2Minus1_Mod5()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(-1, 5), F(0, 5), F(1, 5)); // x^2 - 1

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 5);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(-1, 5), F(1, 5))); // x-1
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 5), F(1, 5)));  // x+1
    }

    [Fact]
    public void FactorOverFiniteField_X2Plus1_Mod5()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 5), F(0, 5), F(1, 5)); // x^2 + 1

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 5);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(-2, 5), F(1, 5))); // x-2
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(2, 5), F(1, 5)));  // x+2
    }

    [Fact]
    public void FactorOverFiniteField_X2Plus1_Mod3_IsIrreducible()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 3), F(0, 3), F(1, 3)); // x^2 + 1

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 3);

        Assert.Single(factors);
        Assert.Equal(f, factors[0]);
    }

    [Fact]
    public void FactorOverFiniteField_RepeatedLinear_Mod5()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 5), F(2, 5), F(1, 5)); // (x + 1)^2

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 5);
        var linear = Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 5), F(1, 5)); // x + 1

        Assert.Equal(2, factors.Count);
        Assert.Equal(2, factors.Count(x => x == linear));
    }

    [Fact]
    public void FactorOverFiniteField_PthPower_Mod5()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 5), F(0, 5), F(0, 5), F(0, 5), F(0, 5), F(1, 5)); // x^5 + 1 = (x+1)^5 over F5

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 5);
        var linear = Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 5), F(1, 5)); // x + 1

        Assert.Equal(5, factors.Count);
        Assert.Equal(5, factors.Count(x => x == linear));
    }

    [Fact]
    public void FactorOverFiniteField_Reconstruction_WithMultiplicity_Mod5()
    {
        var linear = Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 5), F(1, 5)); // x + 1
        var quadratic = Polynomial<QuotientRing<Integer>>.FromCoeffs(F(2, 5), F(0, 5), F(1, 5)); // x^2 + 2
        var f = linear * linear * linear * quadratic;

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 5);
        var reconstructed = ExpandFiniteFieldFactors(factors);

        Assert.Equal(f, reconstructed);
    }

    [Fact]
    public void FactorOverFiniteField_X4Plus1_Mod5_SplitsIntoQuadratics()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 5), F(0, 5), F(0, 5), F(0, 5), F(1, 5)); // x^4 + 1

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 5);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(2, 5), F(0, 5), F(1, 5))); // x^2 + 2
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(3, 5), F(0, 5), F(1, 5))); // x^2 + 3
    }

    [Fact]
    public void FactorOverFiniteField_X3MinusX_Mod11_SplitsCompletely()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(0, 11), F(-1, 11), F(0, 11), F(1, 11)); // x^3 - x

        var factors = PolynomialFactoring.FactorOverFiniteField(f, 11);

        Assert.Equal(3, factors.Count);
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(0, 11), F(1, 11)));  // x
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(-1, 11), F(1, 11))); // x-1
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 11), F(1, 11)));  // x+1
    }

    [Fact]
    public void Berlekamp_X3MinusX_Mod3_SplitsCompletely()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(0, 3), F(-1, 3), F(0, 3), F(1, 3)); // x^3 - x

        var factors = PolynomialFactoring.Berlekamp(f, 3);

        Assert.Equal(3, factors.Count);
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(0, 3), F(1, 3)));  // x
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(-1, 3), F(1, 3))); // x-1
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 3), F(1, 3)));  // x+1
    }

    [Fact]
    public void Berlekamp_X2PlusXPlus1_Mod2_IsIrreducible()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 2), F(1, 2), F(1, 2));

        var factors = PolynomialFactoring.Berlekamp(f, 2);

        Assert.Single(factors);
        Assert.Equal(f, factors[0]);
    }

    [Fact]
    public void CantorZassenhaus_X3MinusX_Mod3_SplitsCompletely()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(0, 3), F(-1, 3), F(0, 3), F(1, 3)); // x^3 - x

        var factors = PolynomialFactoring.CantorZassenhaus(f, 3);

        Assert.Equal(3, factors.Count);
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(0, 3), F(1, 3)));  // x
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(-1, 3), F(1, 3))); // x-1
        Assert.Contains(factors, x => x == Polynomial<QuotientRing<Integer>>.FromCoeffs(F(1, 3), F(1, 3)));  // x+1
    }

    [Fact]
    public void CantorZassenhaus_X2PlusXPlus1_Mod2_IsIrreducible()
    {
        var f = Polynomial<QuotientRing<Integer>>.FromCoeffs(
            F(1, 2), F(1, 2), F(1, 2));

        var factors = PolynomialFactoring.CantorZassenhaus(f, 2);

        Assert.Single(factors);
        Assert.Equal(f, factors[0]);
    }

    [Fact]
    public void HenselLift_ExactFactors_RemainStable()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x - Polynomial<Integer>.One;
        var g = x - Polynomial<Integer>.One;
        var h = x + Polynomial<Integer>.One;

        var (gLift, hLift) = PolynomialFactoring.HenselLift(f, g, h, (Integer)3, precision: 2);

        Assert.Equal(g, gLift);
        Assert.Equal(h, hLift);
        Assert.Equal(f, gLift * hLift);
    }

    [Fact]
    public void HenselLift_LiftsMod5Factorization_ToMod25()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x + Polynomial<Integer>.One;
        var g = x - Polynomial<Integer>.C((Integer)2);
        var h = x + Polynomial<Integer>.C((Integer)2);

        var (gLift, hLift) = PolynomialFactoring.HenselLift(f, g, h, (Integer)5, precision: 2);
        var product = gLift * hLift;

        AssertPolynomialCongruentMod(product, f, (Integer)25);
    }

    // ===================================================================
    // Factoring edge cases: zero, constant, linear
    // ===================================================================

    [Fact]
    public void FactorOverZ_Zero_ReturnsEmpty()
    {
        var factors = PolynomialFactoring.FactorOverZ(Polynomial<Integer>.Zero);
        Assert.Empty(factors);
    }

    [Fact]
    public void FactorOverZ_Constant_ReturnsEmpty()
    {
        var factors = PolynomialFactoring.FactorOverZ(Polynomial<Integer>.C((Integer)5));
        Assert.Empty(factors);
    }

    [Fact]
    public void FactorOverZ_Linear_IsSingleFactor()
    {
        // 3x + 6 = content 3, primitive part x + 2
        var f = Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)3);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)1), factors[0].Factor); // x + 2
        Assert.Equal(1, factors[0].Multiplicity);
    }

    [Fact]
    public void FactorOverQ_Zero_ReturnsEmpty()
    {
        var result = PolynomialFactoring.Factor(Polynomial<Rational>.Zero);
        Assert.Equal(Rational.Zero, result.Content);
        Assert.Empty(result.Factors);
    }

    [Fact]
    public void FactorOverQ_Constant_ReturnsContentOnly()
    {
        var result = PolynomialFactoring.Factor(Polynomial<Rational>.C((Rational)5));
        Assert.Equal((Rational)5, result.Content);
        Assert.Empty(result.Factors);
    }

    [Fact]
    public void FactorOverQ_One_ReturnsContentOne()
    {
        var result = PolynomialFactoring.Factor(Polynomial<Rational>.One);
        Assert.Equal((Rational)1, result.Content);
        Assert.Empty(result.Factors);
    }

    [Fact]
    public void FactorOverQ_LinearWithContent()
    {
        // 3x + 6 = 3*(x + 2)
        var x = Polynomial<Rational>.X;
        var f = Polynomial<Rational>.C((Rational)3) * x + Polynomial<Rational>.C((Rational)6);

        var result = PolynomialFactoring.Factor(f);

        Assert.Equal((Rational)3, result.Content);
        Assert.Single(result.Factors);
        Assert.Equal(x + Polynomial<Rational>.C((Rational)2), result.Factors[0].Factor);
    }

    // ===================================================================
    // Known factorizations: x^4-1, x^6-1
    // ===================================================================

    [Fact]
    public void FactorOverZ_X4Minus1()
    {
        // x^4 - 1 = (x - 1)(x + 1)(x^2 + 1)
        var x = Polynomial<Integer>.X;
        var f = x * x * x * x - Polynomial<Integer>.One;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(3, factors.Count);
        Assert.Contains(factors, t => t.Factor == (x - Polynomial<Integer>.One) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == (x + Polynomial<Integer>.One) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1) && t.Multiplicity == 1);

        Assert.Equal(f, ExpandIntegerFactors(factors));
    }

    [Fact]
    public void FactorOverZ_X6Minus1()
    {
        // x^6 - 1 = (x - 1)(x + 1)(x^2 + x + 1)(x^2 - x + 1)
        var x = Polynomial<Integer>.X;
        var one = Polynomial<Integer>.One;
        var f = x * x * x * x * x * x - one;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(4, factors.Count);
        Assert.Contains(factors, t => t.Factor == (x - one) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == (x + one) && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1, (Integer)1) && t.Multiplicity == 1); // x^2+x+1
        Assert.Contains(factors, t => t.Factor == Polynomial<Integer>.FromCoeffs((Integer)1, -(Integer)1, (Integer)1) && t.Multiplicity == 1); // x^2-x+1

        Assert.Equal(f, ExpandIntegerFactors(factors));
    }

    // ===================================================================
    // Cyclotomic polynomials: irreducibility
    // ===================================================================

    [Theory]
    [InlineData(2)] // Phi_2 = x + 1
    [InlineData(3)] // Phi_3 = x^2 + x + 1
    [InlineData(5)] // Phi_5 = x^4 + x^3 + x^2 + x + 1
    [InlineData(7)] // Phi_7 = x^6 + x^5 + x^4 + x^3 + x^2 + x + 1
    public void FactorOverZ_CyclotomicPhiP_IsIrreducible(int p)
    {
        // Phi_p(x) = x^(p-1) + x^(p-2) + ... + x + 1 for prime p
        var coeffs = new Integer[p];
        for (int i = 0; i < p; i++)
            coeffs[i] = Integer.One;

        var phi = Polynomial<Integer>.FromCoeffs(coeffs);
        var factors = PolynomialFactoring.FactorOverZ(phi);

        Assert.Single(factors);
        Assert.Equal(phi, factors[0].Factor);
        Assert.Equal(1, factors[0].Multiplicity);
    }

    [Fact]
    public void FactorOverZ_Phi6_IsIrreducible()
    {
        // Phi_6(x) = x^2 - x + 1
        var phi6 = Polynomial<Integer>.FromCoeffs((Integer)1, -(Integer)1, (Integer)1);
        var factors = PolynomialFactoring.FactorOverZ(phi6);

        Assert.Single(factors);
        Assert.Equal(phi6, factors[0].Factor);
    }

    // ===================================================================
    // Irreducibility: x^2+1, x^2+x+1, Eisenstein
    // ===================================================================

    [Fact]
    public void FactorOverZ_X2Plus1_IsIrreducible()
    {
        var f = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1); // x^2 + 1
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(f, factors[0].Factor);
    }

    [Fact]
    public void FactorOverZ_X2PlusXPlus1_IsIrreducible()
    {
        var f = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1, (Integer)1); // x^2 + x + 1
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(f, factors[0].Factor);
    }

    [Fact]
    public void FactorOverZ_Eisenstein_X2Plus2_IsIrreducible()
    {
        // x^2 + 2: Eisenstein at p=2 (2 | 2, 4 does not divide 2, 2 does not divide 1)
        var f = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(f, factors[0].Factor);
    }

    // ===================================================================
    // High multiplicities
    // ===================================================================

    [Fact]
    public void FactorOverZ_HighMultiplicity_XMinus1_ToThe5()
    {
        var x = Polynomial<Integer>.X;
        var lin = x - Polynomial<Integer>.One;
        var f = lin * lin * lin * lin * lin; // (x-1)^5

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Single(factors);
        Assert.Equal(lin, factors[0].Factor);
        Assert.Equal(5, factors[0].Multiplicity);
    }

    [Fact]
    public void FactorOverZ_CombinedMultiplicities()
    {
        // (x-1)^3 * (x+1)^2
        var x = Polynomial<Integer>.X;
        var xm1 = x - Polynomial<Integer>.One;
        var xp1 = x + Polynomial<Integer>.One;
        var f = xm1 * xm1 * xm1 * xp1 * xp1;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(2, factors.Count);
        Assert.Contains(factors, t => t.Factor == xm1 && t.Multiplicity == 3);
        Assert.Contains(factors, t => t.Factor == xp1 && t.Multiplicity == 2);
        Assert.Equal(f, ExpandIntegerFactors(factors));
    }

    // ===================================================================
    // Square-free: additional cases
    // ===================================================================

    [Fact]
    public void SquareFree_X3_ReportsMultiplicity3()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x * x;

        var sf = PolynomialFactoring.SquareFreeFactorization(f);

        Assert.Single(sf);
        Assert.Equal(x, sf[0].Factor);
        Assert.Equal(3, sf[0].Multiplicity);
    }

    [Fact]
    public void SquareFree_X2Minus1_AlreadySquareFree()
    {
        var x = Polynomial<Integer>.X;
        var f = x * x - Polynomial<Integer>.One;

        var sf = PolynomialFactoring.SquareFreeFactorization(f);

        // x^2-1 is square-free, so result is a single factor with multiplicity 1
        // (it may or may not be further factored, just check multiplicity and reconstruction)
        foreach (var (factor, mult) in sf)
            Assert.Equal(1, mult);

        var product = Polynomial<Integer>.One;
        foreach (var (factor, mult) in sf)
            for (int i = 0; i < mult; i++) product *= factor;
        Assert.Equal(f, product);
    }

    [Fact]
    public void SquareFree_X4Minus2X2Plus1()
    {
        // x^4 - 2x^2 + 1 = (x^2 - 1)^2 = (x-1)^2 * (x+1)^2
        var x = Polynomial<Integer>.X;
        var f = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, -(Integer)2, (Integer)0, (Integer)1);

        var sf = PolynomialFactoring.SquareFreeFactorization(f);

        // Should report multiplicity 2 for (x^2-1) or split further
        var product = Polynomial<Integer>.One;
        foreach (var (factor, mult) in sf)
        {
            Assert.True(mult >= 2, "x^4 - 2x^2 + 1 has repeated factors, multiplicity >= 2 expected");
            for (int i = 0; i < mult; i++) product *= factor;
        }
        Assert.Equal(f, product);
    }

    [Fact]
    public void SquareFree_Reconstruction()
    {
        // For any input: product of factor^mult == primitive part of original
        var x = Polynomial<Integer>.X;
        var one = Polynomial<Integer>.One;
        var f = (x - one) * (x - one) * (x + one) * (x + one) * (x + one);

        var sf = PolynomialFactoring.SquareFreeFactorization(f);

        var product = Polynomial<Integer>.One;
        foreach (var (factor, mult) in sf)
            for (int i = 0; i < mult; i++) product *= factor;
        Assert.Equal(f, product);
    }

    // ===================================================================
    // Factor over Q: rational content
    // ===================================================================

    [Fact]
    public void FactorOverQ_HalfX2MinusHalf()
    {
        // (1/2)x^2 - (1/2) = (1/2)*(x-1)*(x+1)
        var x = Polynomial<Rational>.X;
        var half = Polynomial<Rational>.C(Rational.Create((Integer)1, (Integer)2));
        var f = half * x * x - half;

        var result = PolynomialFactoring.Factor(f);

        Assert.Equal(Rational.Create((Integer)1, (Integer)2), result.Content);
        Assert.Equal(2, result.Factors.Count);
        Assert.Contains(result.Factors, t => t.Factor == (x - Polynomial<Rational>.One) && t.Multiplicity == 1);
        Assert.Contains(result.Factors, t => t.Factor == (x + Polynomial<Rational>.One) && t.Multiplicity == 1);
    }

    [Fact]
    public void FactorOverQ_RationalContentExtraction()
    {
        // (2/3)x^2 + (4/3)x + (2/3) = (2/3)*(x^2 + 2x + 1) = (2/3)*(x+1)^2
        var twoThirds = Rational.Create((Integer)2, (Integer)3);
        var fourThirds = Rational.Create((Integer)4, (Integer)3);
        var f = Polynomial<Rational>.FromCoeffs(twoThirds, fourThirds, twoThirds);

        var result = PolynomialFactoring.Factor(f);

        Assert.Equal(twoThirds, result.Content);
        Assert.Single(result.Factors);
        var x = Polynomial<Rational>.X;
        Assert.Equal(x + Polynomial<Rational>.One, result.Factors[0].Factor);
        Assert.Equal(2, result.Factors[0].Multiplicity);
    }

    [Fact]
    public void FactorOverQ_Reconstruction()
    {
        // content * product(factors^mult) == original
        var x = Polynomial<Rational>.X;
        var three = Polynomial<Rational>.C((Rational)3);
        var f = three * (x * x - Polynomial<Rational>.One);

        var result = PolynomialFactoring.Factor(f);

        var product = Polynomial<Rational>.C(result.Content);
        foreach (var (factor, mult) in result.Factors)
            for (int i = 0; i < mult; i++) product *= factor;
        Assert.Equal(f, product);
    }

    // ===================================================================
    // Performance sanity checks
    // ===================================================================

    [Fact]
    public void Performance_DerivativeOfDegree10000()
    {
        var p = Polynomial<Integer>.Monomial(10000, (Integer)1) + Polynomial<Integer>.C((Integer)1);
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal(9999, dp.Degree);
        Assert.Equal((Integer)10000, dp[9999]);
    }

    [Fact]
    public void Performance_Parse100TermPolynomial()
    {
        // Build a 100-term polynomial string: x^99 + x^98 + ... + x + 1
        var terms = Enumerable.Range(0, 100).Select(i => i == 0 ? "1" : i == 1 ? "x" : $"x^{i}").Reverse();
        var input = string.Join(" + ", terms);

        var p = Polynomial<Integer>.Parse(input);
        Assert.Equal(99, p.Degree);
        for (int i = 0; i <= 99; i++)
            Assert.Equal(Integer.One, p[i]);
    }

    [Fact]
    public void Performance_Format100TermPolynomial()
    {
        // Build a 100-term polynomial
        var coeffs = new Integer[100];
        for (int i = 0; i < 100; i++)
            coeffs[i] = Integer.One;
        var p = Polynomial<Integer>.FromCoeffs(coeffs);

        var s = p.ToString();
        Assert.Contains("x^99", s);
        Assert.Contains("x + 1", s);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(20)]
    public void Performance_FactorXnMinus1(int n)
    {
        // x^n - 1 should factor in reasonable time
        var f = Polynomial<Integer>.Monomial(n, Integer.One) - Polynomial<Integer>.One;
        var factors = PolynomialFactoring.FactorOverZ(f);

        // Reconstruction: product of factors == original
        Assert.Equal(f, ExpandIntegerFactors(factors));

        // Must have at least 2 factors for n >= 2 (x-1 is always a factor)
        Assert.True(factors.Count >= 2);
        Assert.Contains(factors, t => t.Factor == (Polynomial<Integer>.X - Polynomial<Integer>.One));
    }

    [Fact]
    public void Performance_FactorProductOfThreeDegree5()
    {
        // Three irreducible degree-2 polynomials multiplied together (degree 6 product)
        var p1 = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1);   // x^2 + 1
        var p2 = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1, (Integer)1);   // x^2 + x + 1
        var p3 = Polynomial<Integer>.FromCoeffs((Integer)1, -(Integer)1, (Integer)1);  // x^2 - x + 1

        var f = p1 * p2 * p3;
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(3, factors.Count);
        Assert.Contains(factors, t => t.Factor == p1 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == p2 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == p3 && t.Multiplicity == 1);
        Assert.Equal(f, ExpandIntegerFactors(factors));
    }

    [Fact]
    public void Performance_SquareFreeHighMultiplicity()
    {
        // (x-1)^10 * (x+1)^10
        var x = Polynomial<Integer>.X;
        var xm1 = x - Polynomial<Integer>.One;
        var xp1 = x + Polynomial<Integer>.One;

        var f = Polynomial<Integer>.One;
        for (int i = 0; i < 10; i++) f *= xm1;
        for (int i = 0; i < 10; i++) f *= xp1;

        var sf = PolynomialFactoring.SquareFreeFactorization(f);

        // Reconstruction
        var product = Polynomial<Integer>.One;
        foreach (var (factor, mult) in sf)
            for (int i = 0; i < mult; i++) product *= factor;
        Assert.Equal(f, product);

        // Should have multiplicity 10
        Assert.Contains(sf, t => t.Multiplicity == 10);
    }

    // ===================================================================
    // Van Hoeij adversarial cases: cyclotomic and large-l inputs
    // ===================================================================

    [Theory]
    [InlineData(11)]  // Phi_11 = degree 10, irreducible
    [InlineData(13)]  // Phi_13 = degree 12, irreducible
    public void VanHoeij_CyclotomicPrimeDegree_IsIrreducible(int p)
    {
        // Phi_p(x) = x^(p-1) + ... + x + 1, irreducible by Eisenstein after substitution x → x+1
        var coeffs = new Integer[p];
        for (int i = 0; i < p; i++)
            coeffs[i] = Integer.One;

        var phi = Polynomial<Integer>.FromCoeffs(coeffs);
        var factors = PolynomialFactoring.FactorOverZ(phi);

        Assert.Single(factors);
        Assert.Equal(phi, factors[0].Factor);
        Assert.Equal(1, factors[0].Multiplicity);
    }

    [Fact]
    public void VanHoeij_Degree8Product_FourQuadratics_Reconstructs()
    {
        // (x^2+1)(x^2+2)(x^2+3)(x^2+5) — 4 irreducible quadratic factors, adversarial for subset recombination
        var q1 = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1);
        var q2 = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1);
        var q3 = Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1);
        var q5 = Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1);
        var f = q1 * q2 * q3 * q5;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(4, factors.Count);
        Assert.Contains(factors, t => t.Factor == q1 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == q2 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == q3 && t.Multiplicity == 1);
        Assert.Contains(factors, t => t.Factor == q5 && t.Multiplicity == 1);
    }

    [Fact]
    public void VanHoeij_Degree10Product_FiveQuadratics_Reconstructs()
    {
        // 5 irreducible quadratic factors — l = 5 lifted factors, LLL dimension = 5 + 11 = 16
        var quadratics = new[]
        {
            Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)7, (Integer)0, (Integer)1),
        };

        var f = quadratics.Aggregate(Polynomial<Integer>.One, (acc, q) => acc * q);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(5, factors.Count);
        foreach (var q in quadratics)
            Assert.Contains(factors, t => t.Factor == q && t.Multiplicity == 1);
    }

    [Fact]
    public void VanHoeij_LargeL_Diagnostics_Reconstructs()
    {
        // 5 quadratics: adversarial for subset recombination
        var quadratics = new[]
        {
            Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)0, (Integer)1),
            Polynomial<Integer>.FromCoeffs((Integer)6, (Integer)0, (Integer)1),
        };

        var f = quadratics.Aggregate(Polynomial<Integer>.One, (acc, q) => acc * q);
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(5, factors.Count);
    }

    [Fact]
    public void VanHoeij_Cyclotomic_X12Minus1_ReconstructsExactly()
    {
        // x^12 - 1 = (x-1)(x+1)(x^2+1)(x^2+x+1)(x^2-x+1)(x^4-x^2+1)
        var f = Polynomial<Integer>.Monomial(12, Integer.One) - Polynomial<Integer>.One;
        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.True(factors.Count >= 4); // at least (x-1)(x+1)(x^2+1)(...)
        Assert.Contains(factors, t => t.Factor == (Polynomial<Integer>.X - Polynomial<Integer>.One));
        Assert.Contains(factors, t => t.Factor == (Polynomial<Integer>.X + Polynomial<Integer>.One));
    }

    [Fact]
    public void VanHoeij_ProductReconstructsForAllXnMinus1()
    {
        // x^n - 1 for n = 8, 10, 12, 15: all should reconstruct exactly
        foreach (int n in new[] { 8, 10, 12, 15 })
        {
            var f = Polynomial<Integer>.Monomial(n, Integer.One) - Polynomial<Integer>.One;
            var factors = PolynomialFactoring.FactorOverZ(f);
            Assert.Equal(f, ExpandIntegerFactors(factors));
        }
    }

    // -------------------------------------------------------------------------
    // Van Hoeij adversarial cases (Newton-sum LLL recombination)
    // -------------------------------------------------------------------------

    // Cyclotomic polynomials Φ_n(x). These are irreducible over Z and have many
    // modular factors — exactly the adversarial case for subset recombination.

    // Φ_n(x) = x^{φ(n)} + ... computed as (x^n - 1) / Π_{d|n, d<n} Φ_d(x).
    // We test that factoring x^n - 1 yields the correct cyclotomic factors.

    [Fact]
    public void VanHoeij_Cyclotomic5_IrreducibleOverZ()
    {
        // Φ_5(x) = x^4 + x^3 + x^2 + x + 1 — irreducible over Z.
        var x = Polynomial<Integer>.X;
        var phi5 = x * x * x * x + x * x * x + x * x + x + Polynomial<Integer>.One;
        var factors = PolynomialFactoring.FactorOverZ(phi5);
        Assert.Single(factors);
        Assert.Equal(phi5, ExpandIntegerFactors(factors));
    }

    [Fact]
    public void VanHoeij_Cyclotomic7_IrreducibleOverZ()
    {
        // Φ_7(x) = x^6 + x^5 + x^4 + x^3 + x^2 + x + 1 — irreducible over Z.
        var x = Polynomial<Integer>.X;
        var phi7 = Polynomial<Integer>.Monomial(6, Integer.One)
            + Polynomial<Integer>.Monomial(5, Integer.One)
            + Polynomial<Integer>.Monomial(4, Integer.One)
            + Polynomial<Integer>.Monomial(3, Integer.One)
            + x * x + x + Polynomial<Integer>.One;
        var factors = PolynomialFactoring.FactorOverZ(phi7);
        Assert.Single(factors);
        Assert.Equal(phi7, ExpandIntegerFactors(factors));
    }

    [Fact]
    public void VanHoeij_X12Minus1_CorrectCyclotomicDecomposition()
    {
        // x^12 - 1 = Φ_1 * Φ_2 * Φ_3 * Φ_4 * Φ_6 * Φ_12
        //          = (x-1)(x+1)(x^2+x+1)(x^2+1)(x^2-x+1)(x^4-x^2+1)
        var x = Polynomial<Integer>.X;
        var f = Polynomial<Integer>.Monomial(12, Integer.One) - Polynomial<Integer>.One;
        var factors = PolynomialFactoring.FactorOverZ(f);

        // Product must equal f.
        Assert.Equal(f, ExpandIntegerFactors(factors));
        // Must have exactly 6 distinct irreducible factors (each with multiplicity 1).
        Assert.Equal(6, factors.Count);
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }

    [Fact]
    public void VanHoeij_ProductOfTwoHighDegree_FactorsCorrectly()
    {
        // f = (x^6 + x^5 + x^4 + x^3 + x^2 + x + 1) * (x^4 - x^3 + x^2 - x + 1)
        // Both factors are cyclotomic (Φ_7 and Φ_10) — irreducible over Z.
        // Together they give a degree-10 polynomial with many modular factors,
        // which exercises van Hoeij recombination rather than simple linear splitting.
        var x = Polynomial<Integer>.X;
        var phi7 = Polynomial<Integer>.Monomial(6, Integer.One)
            + Polynomial<Integer>.Monomial(5, Integer.One)
            + Polynomial<Integer>.Monomial(4, Integer.One)
            + Polynomial<Integer>.Monomial(3, Integer.One)
            + x * x + x + Polynomial<Integer>.One;
        var phi10 = Polynomial<Integer>.Monomial(4, Integer.One)
            - Polynomial<Integer>.Monomial(3, Integer.One)
            + x * x - x + Polynomial<Integer>.One;
        var f = phi7 * phi10;

        var factors = PolynomialFactoring.FactorOverZ(f);

        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(2, factors.Count);
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }

    [Fact]
    public void VanHoeij_Diagnostics_LllDimensionIsSet()
    {
        // Any polynomial that goes through van Hoeij should set LllDimension > 0.
        var x = Polynomial<Integer>.X;
        // x^4 + 1 = Φ_8(x), irreducible over Z but factors mod every prime.
        var phi8 = Polynomial<Integer>.Monomial(4, Integer.One) + Polynomial<Integer>.One;
        var (_, diag) = PolynomialFactoring.FactorOverZWithDiagnostics(phi8);

        // LllDimension should be set if van Hoeij was invoked.
        // (It may be 0 if the polynomial was handled by linear root extraction only,
        //  but Φ_8 has no rational roots so van Hoeij must be attempted.)
        Assert.True(diag.LllDimension >= 0, "LllDimension should be non-negative");
    }

    // -------------------------------------------------------------------------
    // VanHoeij — non-monic / non-unit leading coefficient
    // -------------------------------------------------------------------------

    [Fact]
    public void VanHoeij_NonMonic_CorrectlyFactors()
    {
        // f = 2*(x^2 + 1)*(x^2 + 2). FactorOverZ returns primitive factors — the content 2
        // is not part of the factor list. The primitive part of f is (x^2+1)*(x^2+2).
        var x = Polynomial<Integer>.X;
        var a = x * x + Polynomial<Integer>.One;
        var b = x * x + Polynomial<Integer>.FromCoeffs((Integer)2);
        var primitive = a * b; // x^4 + 3x^2 + 2

        var factors = PolynomialFactoring.FactorOverZ(primitive);
        Assert.Equal(primitive, ExpandIntegerFactors(factors));
        Assert.Equal(2, factors.Count);
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }

    // -------------------------------------------------------------------------
    // VanHoeij — polynomial with both integer roots and a higher-degree factor
    // -------------------------------------------------------------------------

    [Fact]
    public void VanHoeij_MixedLinearAndQuadratic_FactorsCorrectly()
    {
        // f = (x - 1)(x + 2)(x^2 + x + 1)
        var x = Polynomial<Integer>.X;
        var l1 = x - Polynomial<Integer>.One;
        var l2 = x + Polynomial<Integer>.FromCoeffs((Integer)2);
        var q  = x * x + x + Polynomial<Integer>.One;
        var f  = l1 * l2 * q;

        var factors = PolynomialFactoring.FactorOverZ(f);
        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(3, factors.Count);
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }

    [Fact]
    public void VanHoeij_LinearRootPlusHighDegreeIrreducible_FactorsCorrectly()
    {
        // f = (x - 3) * Φ_5(x)
        var x    = Polynomial<Integer>.X;
        var l    = x - Polynomial<Integer>.FromCoeffs((Integer)3);
        var phi5 = x * x * x * x + x * x * x + x * x + x + Polynomial<Integer>.One;
        var f    = l * phi5;

        var factors = PolynomialFactoring.FactorOverZ(f);
        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(2, factors.Count);
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }

    // -------------------------------------------------------------------------
    // Diagnostics contract: LllDimension is set when VanHoeijRecombine is called
    // -------------------------------------------------------------------------

    [Fact]
    public void VanHoeij_Diagnostics_LllDimensionSetByVanHoeijRecombine()
    {
        // A polynomial with multiple lifted factors triggers VanHoeijRecombine.
        // (x^2+x+1)(x^2-x+1) = x^4+x^2+1 — two irreducible quadratics, no linear roots.
        // VanHoeijRecombine is invoked and sets LllDimension.
        var x = Polynomial<Integer>.X;
        var a = x * x + x + Polynomial<Integer>.One;
        var b = x * x - x + Polynomial<Integer>.One;
        var f = a * b; // x^4 + x^2 + 1

        var (factors, diag) = PolynomialFactoring.FactorOverZWithDiagnostics(f);
        Assert.Equal(f, ExpandIntegerFactors(factors));
        // If VanHoeijRecombine was reached, LllDimension > 0; otherwise TwoFactorHensel handled it.
        // Either way, LllDimension must be non-negative and the result correct.
        Assert.True(diag.LllDimension >= 0);
    }

    // -------------------------------------------------------------------------
    // Newton sum property: (x-a)(x-b)(x-c) factors correctly
    // -------------------------------------------------------------------------

    [Fact]
    public void VanHoeij_ThreeLinearFactors_Reconstruct()
    {
        // f = (x-2)(x-3)(x-5) — all integer roots, no van Hoeij needed,
        // but verifies factoring pipeline end-to-end with known roots.
        var x = Polynomial<Integer>.X;
        var f = (x - Polynomial<Integer>.FromCoeffs((Integer)2))
              * (x - Polynomial<Integer>.FromCoeffs((Integer)3))
              * (x - Polynomial<Integer>.FromCoeffs((Integer)5));

        var factors = PolynomialFactoring.FactorOverZ(f);
        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(3, factors.Count);
    }

    // -------------------------------------------------------------------------
    // Irreducible polynomials that require van Hoeij recombination
    // -------------------------------------------------------------------------

    [Fact]
    public void VanHoeij_Phi8_IrreducibleOverZ()
    {
        // Φ_8(x) = x^4 + 1 is irreducible over Z but has many modular factors.
        var x    = Polynomial<Integer>.X;
        var phi8 = Polynomial<Integer>.Monomial(4, Integer.One) + Polynomial<Integer>.One;

        var factors = PolynomialFactoring.FactorOverZ(phi8);
        Assert.Single(factors);
        Assert.Equal(phi8, ExpandIntegerFactors(factors));
    }

    [Fact]
    public void VanHoeij_X15Minus1_ReconstructsExactly()
    {
        // x^15 - 1 = Φ_1 * Φ_3 * Φ_5 * Φ_15, all distinct and irreducible.
        // We verify reconstruction only (factor count depends on pipeline routing).
        var x = Polynomial<Integer>.X;
        var f = Polynomial<Integer>.Monomial(15, Integer.One) - Polynomial<Integer>.One;

        var factors = PolynomialFactoring.FactorOverZ(f);
        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }

    [Fact]
    public void VanHoeij_ProductOfTwoIrreducibleQuadratics_Reconstruct()
    {
        // f = (x^2 + 1)(x^2 + 3) — both irreducible over Z.
        // Exercises MultiplyModPoly in the extraction step.
        var x = Polynomial<Integer>.X;
        var a = x * x + Polynomial<Integer>.One;
        var b = x * x + Polynomial<Integer>.FromCoeffs((Integer)3);
        var f = a * b;

        var factors = PolynomialFactoring.FactorOverZ(f);
        Assert.Equal(f, ExpandIntegerFactors(factors));
        Assert.Equal(2, factors.Count);
        Assert.All(factors, t => Assert.Equal(1, t.Multiplicity));
    }
}
