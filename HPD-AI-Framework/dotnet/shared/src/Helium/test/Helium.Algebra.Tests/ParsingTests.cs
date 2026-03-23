using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class ParsingTests
{
    // --- Polynomial<Integer> ---

    [Fact]
    public void Polynomial_Integer_SingleVariable()
    {
        Assert.Equal(Polynomial<Integer>.X, Polynomial<Integer>.Parse("x"));
        Assert.Equal(Polynomial<Integer>.Monomial(2, (Integer)1), Polynomial<Integer>.Parse("x^2"));
        Assert.Equal(Polynomial<Integer>.Monomial(2, (Integer)3), Polynomial<Integer>.Parse("3x^2"));

        var p = Polynomial<Integer>.Parse("3x^2 + 5x + 1");
        Assert.Equal(2, p.Degree);
        Assert.Equal((Integer)3, p[2]);
        Assert.Equal((Integer)5, p[1]);
        Assert.Equal((Integer)1, p[0]);

        Assert.Equal(Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)1), Polynomial<Integer>.Parse("x^2 - 1"));
        Assert.Equal(Polynomial<Integer>.C((Integer)1), Polynomial<Integer>.Parse("1"));
        Assert.Equal(Polynomial<Integer>.Zero, Polynomial<Integer>.Parse("0"));
        Assert.Equal(-Polynomial<Integer>.X, Polynomial<Integer>.Parse("-x"));
        Assert.Equal(Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, -(Integer)3), Polynomial<Integer>.Parse("-3x^2 + 1"));
    }

    [Fact]
    public void Polynomial_Integer_WhitespaceTolerance()
    {
        var a = Polynomial<Integer>.Parse("3x^2+5x+1");
        var b = Polynomial<Integer>.Parse("  3x^2  +  5x  +  1  ");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Polynomial_Integer_ImplicitMultiplication()
    {
        Assert.Equal(Polynomial<Integer>.Parse("3*x"), Polynomial<Integer>.Parse("3x"));
    }

    [Fact]
    public void Polynomial_Integer_Roundtrip_DefaultToString()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, -(Integer)3, (Integer)2); // 2x^2 - 3x + 1
        var s = p.ToString();
        var q = Polynomial<Integer>.Parse(s);
        Assert.Equal(p, q);
        Assert.Equal(s, q.ToString());
    }

    [Fact]
    public void Polynomial_Integer_InvalidInputs_FailGracefully()
    {
        Assert.False(Polynomial<Integer>.TryParse("", null, out _));
        Assert.False(Polynomial<Integer>.TryParse("3x^2 +", null, out _));
        Assert.False(Polynomial<Integer>.TryParse("x^-1", null, out _));
    }

    // --- Polynomial<Rational> ---

    [Fact]
    public void Polynomial_Rational_CoefficientVariations()
    {
        var a = Polynomial<Rational>.Parse("2/3 x^2");
        var b = Polynomial<Rational>.Parse("(2/3)x^2");
        Assert.Equal(a, b);
        Assert.Equal(Rational.Create((Integer)2, (Integer)3), a[2]);
    }

    [Fact]
    public void Polynomial_Rational_Roundtrip_ParensCoefficient()
    {
        // ToString() includes parentheses for rational coefficients before variables.
        var p = Polynomial<Rational>.Monomial(1, Rational.Create((Integer)3, (Integer)4)) + Polynomial<Rational>.C((Rational)1);
        var s = p.ToString(); // "(3/4)x + 1"
        var q = Polynomial<Rational>.Parse(s);
        Assert.Equal(p, q);
        Assert.Equal(s, q.ToString());
    }

    // --- Matrix<Integer> ---

    [Fact]
    public void Matrix_Integer_BracketNotation()
    {
        var m = Matrix<Integer>.Parse("[[1, 2], [3, 4]]");
        var expected = Matrix<Integer>.FromRows([
            [(Integer)1, (Integer)2],
            [(Integer)3, (Integer)4]
        ]);
        Assert.Equal(expected, m);
    }

    [Fact]
    public void Matrix_Integer_MatlabNotation()
    {
        var m = Matrix<Integer>.Parse("[1, 2; 3, 4]");
        var expected = Matrix<Integer>.FromRows([
            [(Integer)1, (Integer)2],
            [(Integer)3, (Integer)4]
        ]);
        Assert.Equal(expected, m);
    }

    [Fact]
    public void Matrix_Integer_Roundtrip_DefaultToString()
    {
        var m = Matrix<Integer>.Identity(3);
        var s = m.ToString();
        var parsed = Matrix<Integer>.Parse(s);
        Assert.Equal(m, parsed);
    }

    [Fact]
    public void Matrix_Integer_Ragged_Fails()
    {
        Assert.False(Matrix<Integer>.TryParse("[[1, 2], [3]]", null, out _));
    }

    [Fact]
    public void Matrix_Integer_EmptyRow_IsAllowed()
    {
        var m = Matrix<Integer>.Parse("[[]]");
        Assert.Equal(1, m.Rows);
        Assert.Equal(0, m.Cols);
    }

    // --- MvPolynomial<Integer> ---

    [Fact]
    public void MvPolynomial_Integer_MultiVariable()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var z = MvPolynomial<Integer>.Var(2);

        var a = MvPolynomial<Integer>.Parse("x^2 + y^2");
        Assert.Equal(x * x + y * y, a);

        var b = MvPolynomial<Integer>.Parse("3x^2*y + 2x*y^2 + 1");
        var expectedB =
            MvPolynomial<Integer>.C((Integer)3) * x * x * y +
            MvPolynomial<Integer>.C((Integer)2) * x * y * y +
            MvPolynomial<Integer>.C((Integer)1);
        Assert.Equal(expectedB, b);

        var c = MvPolynomial<Integer>.Parse("x*y*z");
        Assert.Equal(x * y * z, c);
    }

    [Fact]
    public void MvPolynomial_Integer_VariableIdentification()
    {
        Assert.Equal(MvPolynomial<Integer>.Var(0), MvPolynomial<Integer>.Parse("x"));
        Assert.Equal(MvPolynomial<Integer>.Var(1), MvPolynomial<Integer>.Parse("y"));
        Assert.Equal(MvPolynomial<Integer>.Var(2), MvPolynomial<Integer>.Parse("z"));
        Assert.Equal(MvPolynomial<Integer>.Var(0), MvPolynomial<Integer>.Parse("x0"));
        Assert.Equal(MvPolynomial<Integer>.Var(1), MvPolynomial<Integer>.Parse("x1"));
        Assert.Equal(MvPolynomial<Integer>.Var(2), MvPolynomial<Integer>.Parse("x2"));
        Assert.Equal(MvPolynomial<Integer>.Var(3), MvPolynomial<Integer>.Parse("x3"));
    }

    [Fact]
    public void MvPolynomial_Integer_Roundtrip_DefaultToString()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var p = MvPolynomial<Integer>.C((Integer)2) * x * y + MvPolynomial<Integer>.C((Integer)1);

        var s = p.ToString(); // "2xy + 1"
        var q = MvPolynomial<Integer>.Parse(s);
        Assert.Equal(p, q);
        Assert.Equal(s, q.ToString());
    }

    // --- MvPolynomial<Rational> ---

    [Fact]
    public void MvPolynomial_Rational_Roundtrip()
    {
        var x = MvPolynomial<Rational>.Var(0);
        var y = MvPolynomial<Rational>.Var(1);
        var half = MvPolynomial<Rational>.C(Rational.Create((Integer)1, (Integer)2));
        var p = half * x * x + MvPolynomial<Rational>.C((Rational)3) * y + MvPolynomial<Rational>.C((Rational)1);

        var s = p.ToString();
        var q = MvPolynomial<Rational>.Parse(s);
        Assert.Equal(p, q);
        Assert.Equal(s, q.ToString());
    }
}

