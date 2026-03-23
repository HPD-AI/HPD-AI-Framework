using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;
using Double = Helium.Primitives.Double;

namespace Helium.Algorithms.Tests;

public class ForwardDiffTests
{
    // Shorthand: R(n) = (Rational)n
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);

    // --- Diff: single-variable exact derivatives over Rational ---

    [Fact]
    public void Diff_Identity()
    {
        // f(x) = x → f'(x) = 1
        var d = ForwardDiff.Diff<Rational>(x => x, R(5));
        Assert.Equal(R(1), d);
    }

    [Fact]
    public void Diff_Constant()
    {
        // f(x) = 7 → f'(x) = 0
        var d = ForwardDiff.Diff<Rational>(
            x => FormalPowerSeries<Rational>.Constant(R(7)), R(3));
        Assert.Equal(R(0), d);
    }

    [Fact]
    public void Diff_Scale()
    {
        // f(x) = 3x → f'(x) = 3
        var d = ForwardDiff.Diff<Rational>(
            x => FormalPowerSeries<Rational>.Constant(R(3)) * x, R(2));
        Assert.Equal(R(3), d);
    }

    [Fact]
    public void Diff_Quadratic_At3()
    {
        // f(x) = x² → f'(x) = 2x, f'(3) = 6
        var d = ForwardDiff.Diff<Rational>(x => x * x, R(3));
        Assert.Equal(R(6), d);
    }

    [Fact]
    public void Diff_Quadratic_AtZero()
    {
        // f(x) = x² → f'(0) = 0
        var d = ForwardDiff.Diff<Rational>(x => x * x, R(0));
        Assert.Equal(R(0), d);
    }

    [Fact]
    public void Diff_Cubic_At2()
    {
        // f(x) = x³ → f'(x) = 3x², f'(2) = 12
        var d = ForwardDiff.Diff<Rational>(x => x * x * x, R(2));
        Assert.Equal(R(12), d);
    }

    [Fact]
    public void Diff_SumRule()
    {
        // f(x) = x² + x → f'(x) = 2x + 1, f'(3) = 7
        var d = ForwardDiff.Diff<Rational>(
            x => x * x + x, R(3));
        Assert.Equal(R(7), d);
    }

    [Fact]
    public void Diff_AffinePlusSq()
    {
        // f(x) = x² - 4x + 1 → f'(x) = 2x - 4, f'(1) = -2
        var c4 = FormalPowerSeries<Rational>.Constant(R(4));
        var c1 = FormalPowerSeries<Rational>.Constant(R(1));
        var d = ForwardDiff.Diff<Rational>(
            x => x * x - c4 * x + c1, R(1));
        Assert.Equal(R(-2), d);
    }

    [Fact]
    public void Diff_ProductRule()
    {
        // f(x) = (x+1)(x-1) = x²-1 → f'(x) = 2x, f'(3) = 6
        var one = FormalPowerSeries<Rational>.Constant(R(1));
        var d = ForwardDiff.Diff<Rational>(
            x => (x + one) * (x - one), R(3));
        Assert.Equal(R(6), d);
    }

    [Fact]
    public void Diff_Inverse_At2()
    {
        // f(x) = 1/x → f'(x) = -1/x², f'(2) = -1/4
        var d = ForwardDiff.Diff<Rational>(
            x => FormalPowerSeries<Rational>.One / x, R(2));
        Assert.Equal(R(-1, 4), d);
    }

    [Fact]
    public void Diff_Over_Double()
    {
        // f(x) = x² → f'(3.0) ≈ 6.0
        var d = ForwardDiff.Diff<Double>(x => x * x, new Double(3.0));
        Assert.Equal(new Double(6.0), d);
    }

    // --- ValueAndDiff ---

    [Fact]
    public void ValueAndDiff_Quadratic()
    {
        // f(x) = x² at x=3 → (9, 6)
        var (v, dv) = ForwardDiff.ValueAndDiff<Rational>(x => x * x, R(3));
        Assert.Equal(R(9), v);
        Assert.Equal(R(6), dv);
    }

    [Fact]
    public void ValueAndDiff_Identity()
    {
        var (v, dv) = ForwardDiff.ValueAndDiff<Rational>(x => x, R(7));
        Assert.Equal(R(7), v);
        Assert.Equal(R(1), dv);
    }

    [Fact]
    public void ValueAndDiff_Cubic()
    {
        // f(x) = x³ at x=2 → value=8, deriv=12
        var (v, dv) = ForwardDiff.ValueAndDiff<Rational>(x => x * x * x, R(2));
        Assert.Equal(R(8), v);
        Assert.Equal(R(12), dv);
    }

    // --- Regression: FromInt refactor did not break PolynomialCalculus ---

    [Fact]
    public void Regression_Derivative_Cubic()
    {
        var p = Polynomial<Rational>.FromCoeffs([
            Rational.Zero, (Rational)2, Rational.Zero, Rational.One]);
        var dp = PolynomialCalculus.Derivative(p);
        Assert.Equal((Rational)2, dp[0]);
        Assert.Equal(Rational.Zero, dp[1]);
        Assert.Equal((Rational)3, dp[2]);
    }

    [Fact]
    public void Regression_Integrate_Linear()
    {
        // Integrate(2x) = x²
        var p = Polynomial<Rational>.FromCoeffs([Rational.Zero, (Rational)2]);
        var ip = PolynomialCalculus.Integrate(p);
        Assert.Equal(Rational.Zero, ip[0]);
        Assert.Equal(Rational.Zero, ip[1]);
        Assert.Equal(Rational.One, ip[2]);
    }

    [Fact]
    public void Regression_CharPoly_Companion()
    {
        // [[0,1],[-1,0]] → x² + 1
        var m = Matrix<Integer>.FromArray(2, 2, [
            (Integer)0, (Integer)1,
            (Integer)(-1), (Integer)0]);
        var p = CharacteristicPolynomial.Compute(m);
        Assert.Equal(Integer.One, p[0]);
        Assert.Equal(Integer.Zero, p[1]);
        Assert.Equal(Integer.One, p[2]);
    }
}
