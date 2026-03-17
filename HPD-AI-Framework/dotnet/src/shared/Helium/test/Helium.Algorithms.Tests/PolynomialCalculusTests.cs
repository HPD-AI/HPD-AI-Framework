using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class PolynomialCalculusTests
{
    // --- Derivative (basic) ---

    [Fact]
    public void Derivative_OfXToTheFifth_Is5XToTheFourth()
    {
        var p = Polynomial<Integer>.Monomial(5, (Integer)1);
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal(Polynomial<Integer>.Monomial(4, (Integer)5), dp);
    }

    [Fact]
    public void Derivative_OfConstant_IsZero()
    {
        var p = Polynomial<Integer>.C((Integer)7);
        Assert.Equal(Polynomial<Integer>.Zero, PolynomialCalculus.Derivative(p));
    }

    [Fact]
    public void Derivative_OfZero_IsZero()
    {
        Assert.Equal(Polynomial<Integer>.Zero, PolynomialCalculus.Derivative(Polynomial<Integer>.Zero));
    }

    [Fact]
    public void Derivative_OfLinear_IsConstant()
    {
        // d/dx(3x + 2) = 3
        var p = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)3);
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal(Polynomial<Integer>.C((Integer)3), dp);
    }

    [Fact]
    public void Derivative_OfQuadratic()
    {
        // d/dx(2x^2 + 3x + 1) = 4x + 3
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)3, (Integer)2);
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal(Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)4), dp);
    }

    [Fact]
    public void Derivative_OfX_IsOne()
    {
        Assert.Equal(Polynomial<Integer>.One, PolynomialCalculus.Derivative(Polynomial<Integer>.X));
    }

    // --- Derivative (properties) ---

    [Fact]
    public void Derivative_Linearity()
    {
        // d(a*f + b*g) = a*df + b*dg
        var f = Polynomial<Integer>.FromCoeffs((Integer)0, (Integer)2, (Integer)0, (Integer)1); // x^3 + 2x
        var g = Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)1);             // x^2 - 1
        var a = Polynomial<Integer>.C((Integer)3);
        var b = Polynomial<Integer>.C((Integer)5);

        var lhs = PolynomialCalculus.Derivative(a * f + b * g);
        var rhs = a * PolynomialCalculus.Derivative(f) + b * PolynomialCalculus.Derivative(g);
        Assert.Equal(lhs, rhs);
    }

    [Fact]
    public void Derivative_ProductRule()
    {
        // d(fg) = f*dg + g*df
        var f = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1); // x^2 + 1
        var g = Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)1);             // x + 3

        var lhs = PolynomialCalculus.Derivative(f * g);
        var rhs = f * PolynomialCalculus.Derivative(g) + g * PolynomialCalculus.Derivative(f);
        Assert.Equal(lhs, rhs);
    }

    // --- NthDerivative ---

    [Fact]
    public void NthDerivative_ThirdDerivativeOfXToFifth()
    {
        // d^3/dx^3(x^5) = 5*4*3 * x^2 = 60x^2
        var p = Polynomial<Integer>.Monomial(5, (Integer)1);
        var d3 = PolynomialCalculus.NthDerivative(p, 3);
        Assert.Equal(Polynomial<Integer>.Monomial(2, (Integer)60), d3);
    }

    [Fact]
    public void NthDerivative_ZeroOrder_ReturnsOriginal()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal(p, PolynomialCalculus.NthDerivative(p, 0));
    }

    [Fact]
    public void NthDerivative_ExceedsDegree_ReturnsZero()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3); // degree 2
        Assert.Equal(Polynomial<Integer>.Zero, PolynomialCalculus.NthDerivative(p, 4));
    }

    [Fact]
    public void NthDerivative_NegativeN_Throws()
    {
        var p = Polynomial<Integer>.X;
        Assert.Throws<ArgumentOutOfRangeException>(() => PolynomialCalculus.NthDerivative(p, -1));
    }

    // --- Integrate ---

    [Fact]
    public void Integrate_OfXCubed()
    {
        // integral(x^3 dx) = x^4/4
        var p = Polynomial<Rational>.Monomial(3, (Rational)1);
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal(Rational.Create((Integer)1, (Integer)4), ip[4]);
        Assert.Equal(4, ip.Degree);
    }

    [Fact]
    public void Integrate_OfConstant()
    {
        // integral(5 dx) = 5x
        var p = Polynomial<Rational>.C((Rational)5);
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal((Rational)5, ip[1]);
        Assert.True(ip[0].IsZero);
        Assert.Equal(1, ip.Degree);
    }

    [Fact]
    public void Integrate_OfZero_IsZero()
    {
        Assert.Equal(Polynomial<Rational>.Zero, PolynomialCalculus.Integrate(Polynomial<Rational>.Zero));
    }

    [Fact]
    public void Integrate_ThenDerivative_IsOriginal()
    {
        // d/dx(integral(f)) = f
        var f = Polynomial<Rational>.FromCoeffs((Rational)7, (Rational)2, (Rational)3); // 3x^2 + 2x + 7
        var result = PolynomialCalculus.Derivative(PolynomialCalculus.Integrate(f));
        Assert.Equal(f, result);
    }

    [Fact]
    public void Derivative_ThenIntegrate_IsOriginalMinusConstant()
    {
        // integral(d/dx(f)) = f - f(0)
        var f = Polynomial<Rational>.FromCoeffs((Rational)5, (Rational)2, (Rational)0, (Rational)1); // x^3 + 2x + 5
        var result = PolynomialCalculus.Integrate(PolynomialCalculus.Derivative(f));
        // Expected: x^3 + 2x (no constant term)
        var expected = Polynomial<Rational>.FromCoeffs((Rational)0, (Rational)2, (Rational)0, (Rational)1);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Integrate_OfQuadratic()
    {
        // integral(6x^2 + 4x + 1 dx) = 2x^3 + 2x^2 + x
        var p = Polynomial<Rational>.FromCoeffs((Rational)1, (Rational)4, (Rational)6);
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal((Rational)1, ip[1]); // 1/1
        Assert.Equal((Rational)2, ip[2]); // 4/2
        Assert.Equal((Rational)2, ip[3]); // 6/3
        Assert.Equal(3, ip.Degree);
    }

    // --- PartialDerivative ---

    [Fact]
    public void PartialDerivative_WrtX()
    {
        // d/dx(x^2*y + 2*x*y^2) = 2*x*y + 2*y^2
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var two = MvPolynomial<Integer>.C((Integer)2);
        var p = x * x * y + two * x * y * y;

        var dp = PolynomialCalculus.PartialDerivative(p, 0);

        var expected = two * x * y + two * y * y;
        Assert.Equal(expected, dp);
    }

    [Fact]
    public void PartialDerivative_WrtY()
    {
        // d/dy(x^2*y + 2*x*y^2) = x^2 + 4*x*y
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var two = MvPolynomial<Integer>.C((Integer)2);
        var p = x * x * y + two * x * y * y;

        var dp = PolynomialCalculus.PartialDerivative(p, 1);

        var four = MvPolynomial<Integer>.C((Integer)4);
        var expected = x * x + four * x * y;
        Assert.Equal(expected, dp);
    }

    [Fact]
    public void PartialDerivative_WrtAbsentVariable_IsZero()
    {
        // d/dz(x^2 + y) = 0
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var p = x * x + y;

        Assert.Equal(MvPolynomial<Integer>.Zero, PolynomialCalculus.PartialDerivative(p, 2));
    }

    [Fact]
    public void PartialDerivative_OfConstant_IsZero()
    {
        var p = MvPolynomial<Integer>.C((Integer)5);
        Assert.Equal(MvPolynomial<Integer>.Zero, PolynomialCalculus.PartialDerivative(p, 0));
    }

    [Fact]
    public void PartialDerivative_OfZero_IsZero()
    {
        Assert.Equal(MvPolynomial<Integer>.Zero,
            PolynomialCalculus.PartialDerivative(MvPolynomial<Integer>.Zero, 0));
    }

    // --- Derivative (additional from test spec) ---

    [Fact]
    public void Derivative_OfXSquared_Is2X()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)0, (Integer)0, (Integer)1); // x^2
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal(Polynomial<Integer>.FromCoeffs((Integer)0, (Integer)2), dp); // 2x
    }

    [Fact]
    public void Derivative_SubtractionLinearity()
    {
        // d(f - g) = df - dg
        var f = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1); // x^2 + 1
        var g = Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)0, (Integer)1); // x^3 - 1

        var lhs = PolynomialCalculus.Derivative(f - g);
        var rhs = PolynomialCalculus.Derivative(f) - PolynomialCalculus.Derivative(g);
        Assert.Equal(lhs, rhs);
    }

    [Fact]
    public void Derivative_ProductRule_PowerCheck()
    {
        // f=x^2, g=x^3: d(fg) = d(x^5) = 5x^4
        var f = Polynomial<Integer>.FromCoeffs((Integer)0, (Integer)0, (Integer)1); // x^2
        var g = Polynomial<Integer>.FromCoeffs((Integer)0, (Integer)0, (Integer)0, (Integer)1); // x^3

        var dfg = PolynomialCalculus.Derivative(f * g);
        Assert.Equal(Polynomial<Integer>.Monomial(4, (Integer)5), dfg);

        // Also verify product rule: f*dg + g*df
        var rhs = f * PolynomialCalculus.Derivative(g) + g * PolynomialCalculus.Derivative(f);
        Assert.Equal(dfg, rhs);
    }

    [Fact]
    public void Derivative_DegreeDropsByOne()
    {
        // Degree(df) == Degree(f) - 1 for non-constant f
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3, (Integer)4); // 4x^3+3x^2+2x+1
        Assert.Equal(3, p.Degree);
        Assert.Equal(2, PolynomialCalculus.Derivative(p).Degree);
    }

    [Fact]
    public void Derivative_OverRational()
    {
        // Derivative of Polynomial<Rational>: coefficients stay in Rational
        var p = Polynomial<Rational>.FromCoeffs(
            Rational.Create((Integer)1, (Integer)2),
            (Rational)0,
            Rational.Create((Integer)3, (Integer)4)); // (3/4)x^2 + 1/2
        var dp = PolynomialCalculus.Derivative(p);
        // d/dx((3/4)x^2 + 1/2) = (3/2)x
        Assert.Equal(Rational.Create((Integer)3, (Integer)2), dp[1]);
        Assert.Equal(1, dp.Degree);
    }

    [Fact]
    public void NthDerivative_SecondDerivativeOfCubic()
    {
        // d^2/dx^2(x^3) = 6x
        var p = Polynomial<Integer>.Monomial(3, (Integer)1);
        var d2 = PolynomialCalculus.NthDerivative(p, 2);
        Assert.Equal(Polynomial<Integer>.FromCoeffs((Integer)0, (Integer)6), d2);
    }

    [Fact]
    public void NthDerivative_NthOfXToN_IsFactorial()
    {
        // d^4/dx^4(x^4) = 4! = 24
        var p = Polynomial<Integer>.Monomial(4, (Integer)1);
        var d4 = PolynomialCalculus.NthDerivative(p, 4);
        Assert.Equal(Polynomial<Integer>.C((Integer)24), d4);
    }

    [Fact]
    public void NthDerivative_FirstMatchesDerivative()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)3, (Integer)2); // 2x^2+3x+1
        Assert.Equal(PolynomialCalculus.Derivative(p), PolynomialCalculus.NthDerivative(p, 1));
    }

    [Fact]
    public void Derivative_SparsePolynomial()
    {
        // x^1000 + 1: derivative is 1000*x^999
        var p = Polynomial<Integer>.Monomial(1000, (Integer)1) + Polynomial<Integer>.C((Integer)1);
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal(999, dp.Degree);
        Assert.Equal((Integer)1000, dp[999]);
    }

    // --- Integrate (additional from test spec) ---

    [Fact]
    public void Integrate_OfOne_IsX()
    {
        var p = Polynomial<Rational>.One;
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal((Rational)1, ip[1]);
        Assert.Equal(1, ip.Degree);
    }

    [Fact]
    public void Integrate_OfX_IsXSquaredOverTwo()
    {
        var p = Polynomial<Rational>.X;
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal(Rational.Create((Integer)1, (Integer)2), ip[2]);
        Assert.Equal(2, ip.Degree);
    }

    [Fact]
    public void Integrate_LinearPolynomial()
    {
        // integral(6x + 5) = 3x^2 + 5x
        var p = Polynomial<Rational>.FromCoeffs((Rational)5, (Rational)6);
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal((Rational)5, ip[1]); // 5/1
        Assert.Equal((Rational)3, ip[2]); // 6/2
        Assert.Equal(2, ip.Degree);
    }

    [Fact]
    public void Integrate_Linearity()
    {
        // integral(f + g) == integral(f) + integral(g)
        var f = Polynomial<Rational>.FromCoeffs((Rational)1, (Rational)2); // 2x + 1
        var g = Polynomial<Rational>.FromCoeffs((Rational)3, (Rational)0, (Rational)1); // x^2 + 3

        var lhs = PolynomialCalculus.Integrate(f + g);
        var rhs = PolynomialCalculus.Integrate(f) + PolynomialCalculus.Integrate(g);
        Assert.Equal(lhs, rhs);
    }

    // --- PartialDerivative (additional from test spec) ---

    [Fact]
    public void PartialDerivative_WrtZ_OfXPlusYPlusZ()
    {
        // d/dz(x + y + z) = 1
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var z = MvPolynomial<Integer>.Var(2);
        var p = x + y + z;

        var dp = PolynomialCalculus.PartialDerivative(p, 2);
        Assert.Equal(MvPolynomial<Integer>.One, dp);
    }

    [Fact]
    public void PartialDerivative_MixedPartialsCommute()
    {
        // d/dx(d/dy(f)) == d/dy(d/dx(f)) for f = x^2*y^2 + x*y
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var p = x * x * y * y + x * y;

        var dxdy = PolynomialCalculus.PartialDerivative(PolynomialCalculus.PartialDerivative(p, 1), 0);
        var dydx = PolynomialCalculus.PartialDerivative(PolynomialCalculus.PartialDerivative(p, 0), 1);
        Assert.Equal(dxdy, dydx);
    }

    // --- FormalDerivative (additional from test spec) ---

    [Fact]
    public void FormalDerivative_ConstantSeries_IsZero()
    {
        // Constant series c + 0x + 0x^2 + ... has derivative 0
        var f = FormalPowerSeries<Rational>.Constant((Rational)5);
        var df = PolynomialCalculus.FormalDerivative(f);

        for (int n = 0; n < 5; n++)
            Assert.Equal(Rational.Zero, df.Coefficient(n));
    }

    [Fact]
    public void FormalDerivative_OfX_IsOne()
    {
        // Series 0 + 1*x + 0*x^2 + ... has derivative 1 + 0 + 0 + ...
        var f = FormalPowerSeries<Rational>.X;
        var df = PolynomialCalculus.FormalDerivative(f);

        Assert.Equal((Rational)1, df.Coefficient(0));
        for (int n = 1; n < 5; n++)
            Assert.Equal(Rational.Zero, df.Coefficient(n));
    }

    // --- FormalDerivative ---

    [Fact]
    public void FormalDerivative_GeometricSeries()
    {
        // 1/(1-x) has coefficients all 1. Its derivative 1/(1-x)^2 has coefficients n+1.
        var f = FormalPowerSeries<Rational>.FromGenerator(_ => (Rational)1);
        var df = PolynomialCalculus.FormalDerivative(f);

        for (int n = 0; n < 6; n++)
            Assert.Equal(Rational.FromInteger((Integer)(n + 1)), df.Coefficient(n));
    }

    [Fact]
    public void FormalDerivative_CoefficientFormula()
    {
        // f'[n] = (n+1) * f[n+1]
        var f = FormalPowerSeries<Rational>.FromGenerator(n =>
            Rational.Create((Integer)(n + 1), (Integer)1)); // f[n] = n+1
        var df = PolynomialCalculus.FormalDerivative(f);

        // f'[n] = (n+1) * f[n+1] = (n+1) * (n+2)
        for (int n = 0; n < 10; n++)
        {
            var expected = Rational.FromInteger((Integer)((n + 1) * (n + 2)));
            Assert.Equal(expected, df.Coefficient(n));
        }
    }

    [Fact]
    public void FormalDerivative_ExponentialSeries()
    {
        // e^x: coefficients 1/n!. Its derivative is itself.
        // f[n] = 1/n!, so f'[n] = (n+1) * f[n+1] = (n+1) * 1/(n+1)! = 1/n!
        long factorial = 1;
        var factorials = new long[8];
        for (int i = 0; i < 8; i++)
        {
            if (i > 0) factorial *= i;
            factorials[i] = factorial;
        }

        var exp = FormalPowerSeries<Rational>.FromGenerator(n =>
            n < 0 || n >= 8 ? Rational.Zero : Rational.Create((Integer)1, (Integer)factorials[n]));

        var dexp = PolynomialCalculus.FormalDerivative(exp);

        // First 6 coefficients of derivative should match original
        for (int n = 0; n < 6; n++)
            Assert.Equal(exp.Coefficient(n), dexp.Coefficient(n));
    }
}
