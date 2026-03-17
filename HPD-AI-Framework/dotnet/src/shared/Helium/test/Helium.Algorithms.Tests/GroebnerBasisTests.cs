using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class GroebnerBasisTests
{
    [Fact]
    public void AllSPolynomials_ReduceToZero()
    {
        // Classic example: I = <x^2 + y - 1, x*y - x>
        var x = MvPolynomial<Rational>.Var(0);
        var y = MvPolynomial<Rational>.Var(1);
        var one = MvPolynomial<Rational>.C((Rational)1);

        var f1 = x * x + y - one;
        var f2 = x * y - x;

        var basis = GroebnerBasis.Compute<Rational>([f1, f2]);

        // Verify: all S-polynomials reduce to zero modulo the basis.
        for (int i = 0; i < basis.Count; i++)
        {
            for (int j = i + 1; j < basis.Count; j++)
            {
                var spoly = GroebnerBasis.SPolynomial(basis[i], basis[j]);
                var remainder = GroebnerBasis.Reduce(spoly, basis);
                Assert.True(remainder.IsZero,
                    $"S-poly of basis[{i}] and basis[{j}] did not reduce to zero.");
            }
        }
    }

    [Fact]
    public void IdealMembership_ElementInIdeal_ReducesToZero()
    {
        var x = MvPolynomial<Rational>.Var(0);
        var y = MvPolynomial<Rational>.Var(1);
        var one = MvPolynomial<Rational>.C((Rational)1);

        var f1 = x * x + y - one;
        var f2 = x * y - x;

        var basis = GroebnerBasis.Compute<Rational>([f1, f2]);

        // f1 is in the ideal it generates.
        Assert.True(GroebnerBasis.IsInIdeal(f1, basis));
        // f2 is in the ideal it generates.
        Assert.True(GroebnerBasis.IsInIdeal(f2, basis));
        // A combination is in the ideal.
        Assert.True(GroebnerBasis.IsInIdeal(x * f1 + y * f2, basis));
    }

    [Fact]
    public void ReducedGroebnerBasis_IsUnique()
    {
        // For a given ideal and monomial order, the reduced Groebner basis is unique.
        // Verify by computing from two different generating sets for the same ideal.
        var x = MvPolynomial<Rational>.Var(0);
        var y = MvPolynomial<Rational>.Var(1);
        var one = MvPolynomial<Rational>.C((Rational)1);

        var f1 = x * x + y - one;
        var f2 = x * y - x;

        // Generating set 1: {f1, f2}
        var basis1 = GroebnerBasis.ComputeReduced<Rational>([f1, f2]);

        // Generating set 2: {f1, f2, f1 + f2} (same ideal, different generators)
        var basis2 = GroebnerBasis.ComputeReduced<Rational>([f1, f2, f1 + f2]);

        Assert.Equal(basis1.Count, basis2.Count);
        for (int i = 0; i < basis1.Count; i++)
            Assert.Equal(basis1[i], basis2[i]);
    }
}
