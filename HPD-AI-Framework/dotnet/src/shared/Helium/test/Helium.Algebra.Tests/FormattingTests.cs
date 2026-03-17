using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class FormattingTests
{
    // --- Polynomial Default ---

    [Fact]
    public void Polynomial_Zero()
    {
        Assert.Equal("0", Polynomial<Integer>.Zero.ToString());
    }

    [Fact]
    public void Polynomial_Constant()
    {
        Assert.Equal("5", Polynomial<Integer>.C((Integer)5).ToString());
    }

    [Fact]
    public void Polynomial_X()
    {
        Assert.Equal("x", Polynomial<Integer>.X.ToString());
    }

    [Fact]
    public void Polynomial_NegativeX()
    {
        Assert.Equal("-x", (-Polynomial<Integer>.X).ToString());
    }

    [Fact]
    public void Polynomial_Standard()
    {
        // 3x^2 + 5x + 1
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3);
        Assert.Equal("3x^2 + 5x + 1", p.ToString());
    }

    [Fact]
    public void Polynomial_SubtractionDisplay()
    {
        // x^3 - 1
        var p = Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)0, (Integer)1);
        Assert.Equal("x^3 - 1", p.ToString());
    }

    [Fact]
    public void Polynomial_CoefficientElision()
    {
        // x^2 + x + 1
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1, (Integer)1);
        Assert.Equal("x^2 + x + 1", p.ToString());
    }

    [Fact]
    public void Polynomial_NegativeLeading()
    {
        // -x^2 + 1
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, -(Integer)1);
        Assert.Equal("-x^2 + 1", p.ToString());
    }

    [Fact]
    public void Polynomial_MixedSigns()
    {
        // 2x^2 - 3x + 1
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, -(Integer)3, (Integer)2);
        Assert.Equal("2x^2 - 3x + 1", p.ToString());
    }

    // --- Polynomial LaTeX ---

    [Fact]
    public void Polynomial_Latex_Standard()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3);
        IFormattable f = p;
        Assert.Equal("3x^{2} + 5x + 1", f.ToString("L", null));
    }

    [Fact]
    public void Polynomial_Latex_SubtractionDisplay()
    {
        var p = Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)0, (Integer)1);
        IFormattable f = p;
        Assert.Equal("x^{3} - 1", f.ToString("L", null));
    }

    // --- Polynomial Unicode ---

    [Fact]
    public void Polynomial_Unicode_Standard()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3);
        IFormattable f = p;
        Assert.Equal("3x\u00B2 + 5x + 1", f.ToString("U", null));
    }

    [Fact]
    public void Polynomial_Unicode_HighDegree()
    {
        var p = Polynomial<Integer>.Monomial(10, (Integer)1);
        IFormattable f = p;
        Assert.Equal("x\u00B9\u2070", f.ToString("U", null));
    }

    [Fact]
    public void Polynomial_MathML_Standard()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3);
        IFormattable f = p;
        Assert.Equal("<mrow><mrow><mn>3</mn><mo>&#x2062;</mo><msup><mi>x</mi><mn>2</mn></msup></mrow><mo>+</mo><mrow><mn>5</mn><mo>&#x2062;</mo><mi>x</mi></mrow><mo>+</mo><mn>1</mn></mrow>", f.ToString("M", null));
    }

    [Fact]
    public void Polynomial_MathML_SubtractionDisplay()
    {
        var p = Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)0, (Integer)1);
        IFormattable f = p;
        Assert.Equal("<mrow><msup><mi>x</mi><mn>3</mn></msup><mo>-</mo><mn>1</mn></mrow>", f.ToString("M", null));
    }

    // --- Polynomial with Rational coefficients ---

    [Fact]
    public void Polynomial_Rational_ConstantNoParens()
    {
        // x^2 + 3/4 — constant term needs no parentheses
        var p = Polynomial<Rational>.FromCoeffs(
            Rational.Create((Integer)3, (Integer)4),
            (Rational)0,
            (Rational)1);
        Assert.Equal("x^2 + 3/4", p.ToString());
    }

    [Fact]
    public void Polynomial_Rational_CoefficientParenthesizes()
    {
        // (3/4)x + 1 — rational before variable needs parentheses
        var p = Polynomial<Rational>.FromCoeffs(
            (Rational)1,
            Rational.Create((Integer)3, (Integer)4));
        Assert.Equal("(3/4)x + 1", p.ToString());
    }

    [Fact]
    public void Polynomial_Rational_LatexFraction()
    {
        // x^2 + 3/4 in LaTeX
        var p = Polynomial<Rational>.FromCoeffs(
            Rational.Create((Integer)3, (Integer)4),
            (Rational)0,
            (Rational)1);
        IFormattable f = p;
        Assert.Equal(@"x^{2} + \frac{3}{4}", f.ToString("L", null));
    }

    // --- Polynomial (additional from test spec) ---

    [Fact]
    public void Polynomial_SparseDisplay()
    {
        // x^100 + 1 — no intervening zero terms
        var p = Polynomial<Integer>.Monomial(100, (Integer)1) + Polynomial<Integer>.C((Integer)1);
        Assert.Equal("x^100 + 1", p.ToString());
    }

    [Fact]
    public void Polynomial_DescendingOrder()
    {
        // x^3 + x^2 + x + 1 — terms in descending degree
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1, (Integer)1, (Integer)1);
        Assert.Equal("x^3 + x^2 + x + 1", p.ToString());
    }

    [Fact]
    public void Polynomial_Latex_NegativeLeading()
    {
        // -x^2 + 1 in LaTeX
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, -(Integer)1);
        IFormattable f = p;
        Assert.Equal("-x^{2} + 1", f.ToString("L", null));
    }

    [Fact]
    public void Polynomial_Latex_RationalCoefficient()
    {
        // (3/4)x^2 in LaTeX: \frac{3}{4}x^{2}
        var p = Polynomial<Rational>.Monomial(2, Rational.Create((Integer)3, (Integer)4));
        IFormattable f = p;
        Assert.Equal(@"\frac{3}{4}x^{2}", f.ToString("L", null));
    }

    // --- Display Consistency (from test spec cross-cutting) ---

    [Fact]
    public void Display_Deterministic()
    {
        // Same input always produces same output
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3);
        Assert.Equal(p.ToString(), p.ToString());
        IFormattable f = p;
        Assert.Equal(f.ToString("L", null), f.ToString("L", null));
    }

    [Fact]
    public void Display_EqualObjects_SameString()
    {
        // Two equal objects via different construction produce same display
        var p1 = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1); // x^2 + 1
        var p2 = Polynomial<Integer>.Monomial(2, (Integer)1) + Polynomial<Integer>.C((Integer)1);
        Assert.Equal(p1, p2);
        Assert.Equal(p1.ToString(), p2.ToString());
    }

    [Fact]
    public void Display_ZeroAlwaysZero()
    {
        Assert.Equal("0", Polynomial<Integer>.Zero.ToString());
        Assert.Equal("0", MvPolynomial<Integer>.Zero.ToString());
        Assert.Equal("0", Rational.Zero.ToString());
        Assert.Equal("0", Integer.Zero.ToString());
    }

    // --- MvPolynomial ---

    [Fact]
    public void MvPolynomial_VariableNames()
    {
        Assert.Equal("x", MvPolynomial<Integer>.Var(0).ToString());
        Assert.Equal("y", MvPolynomial<Integer>.Var(1).ToString());
        Assert.Equal("z", MvPolynomial<Integer>.Var(2).ToString());
    }

    [Fact]
    public void MvPolynomial_Standard()
    {
        // 3x^2 + 2xy + 1
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var three = MvPolynomial<Integer>.C((Integer)3);
        var two = MvPolynomial<Integer>.C((Integer)2);
        var one = MvPolynomial<Integer>.C((Integer)1);

        var p = three * x * x + two * x * y + one;
        Assert.Equal("3x^2 + 2xy + 1", p.ToString());
    }

    [Fact]
    public void MvPolynomial_CoefficientElision()
    {
        // x + y (coefficients are 1)
        var p = MvPolynomial<Integer>.Var(0) + MvPolynomial<Integer>.Var(1);
        Assert.Equal("x + y", p.ToString());
    }

    [Fact]
    public void MvPolynomial_Latex()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var three = MvPolynomial<Integer>.C((Integer)3);
        var p = three * x * x;
        IFormattable f = p;
        Assert.Equal("3x^{2}", f.ToString("L", null));
    }

    [Fact]
    public void MvPolynomial_MathML_Standard()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var three = MvPolynomial<Integer>.C((Integer)3);
        var two = MvPolynomial<Integer>.C((Integer)2);
        var one = MvPolynomial<Integer>.C((Integer)1);

        var p = three * x * x + two * x * y + one;
        IFormattable f = p;
        Assert.Equal("<mrow><mrow><mn>3</mn><mo>&#x2062;</mo><msup><mi>x</mi><mn>2</mn></msup></mrow><mo>+</mo><mrow><mn>2</mn><mo>&#x2062;</mo><mrow><mi>x</mi><mo>&#x2062;</mo><mi>y</mi></mrow></mrow><mo>+</mo><mn>1</mn></mrow>", f.ToString("M", null));
    }

    [Fact]
    public void MvPolynomial_Zero()
    {
        Assert.Equal("0", MvPolynomial<Integer>.Zero.ToString());
    }

    // --- Monomial ---

    [Fact]
    public void Monomial_VariableNaming()
    {
        Assert.Equal("x", Monomial.Variable(0).ToString());
        Assert.Equal("y", Monomial.Variable(1).ToString());
        Assert.Equal("z", Monomial.Variable(2).ToString());
        Assert.Equal("x3", Monomial.Variable(3).ToString());
    }

    [Fact]
    public void Monomial_Latex()
    {
        // x^2 * y
        var m = Monomial.Variable(0) + Monomial.Variable(0) + Monomial.Variable(1);
        IFormattable f = m;
        Assert.Equal("x^{2}y", f.ToString("L", null));
    }

    [Fact]
    public void Monomial_MathML()
    {
        var m = Monomial.Variable(0) + Monomial.Variable(0) + Monomial.Variable(1);
        IFormattable f = m;
        Assert.Equal("<mrow><msup><mi>x</mi><mn>2</mn></msup><mo>&#x2062;</mo><mi>y</mi></mrow>", f.ToString("M", null));
    }

    [Fact]
    public void Monomial_Unicode()
    {
        var m = Monomial.Variable(0) + Monomial.Variable(0) + Monomial.Variable(1);
        IFormattable f = m;
        Assert.Equal("x\u00B2y", f.ToString("U", null));
    }

    // --- Matrix ---

    [Fact]
    public void Matrix_Default()
    {
        var m = Matrix<Integer>.Identity(2);
        Assert.Equal("[[1, 0], [0, 1]]", m.ToString());
    }

    [Fact]
    public void Matrix_Latex()
    {
        var m = Matrix<Integer>.Identity(2);
        IFormattable f = m;
        Assert.Equal(@"\begin{pmatrix} 1 & 0 \\ 0 & 1 \end{pmatrix}", f.ToString("L", null));
    }

    [Fact]
    public void Matrix_3x3_Default()
    {
        var m = Matrix<Integer>.FromRows([
            [(Integer)1, (Integer)2, (Integer)3],
            [(Integer)4, (Integer)5, (Integer)6],
            [(Integer)7, (Integer)8, (Integer)9]
        ]);
        Assert.Equal("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]", m.ToString());
    }

    // --- Vector ---

    [Fact]
    public void Vector_Default()
    {
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal("[1, 2, 3]", v.ToString());
    }

    [Fact]
    public void Vector_Latex()
    {
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2, (Integer)3);
        IFormattable f = v;
        Assert.Equal(@"\begin{pmatrix} 1 \\ 2 \\ 3 \end{pmatrix}", f.ToString("L", null));
    }

    // --- LaTeX Validity ---

    [Fact]
    public void Latex_Polynomial_BalancedBraces()
    {
        // Various polynomials produce balanced braces in LaTeX
        var polys = new[]
        {
            Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3),  // 3x^2 + 5x + 1
            Polynomial<Integer>.Monomial(10, (Integer)1),                         // x^10
            Polynomial<Integer>.FromCoeffs(-(Integer)1, (Integer)0, (Integer)0, (Integer)1), // x^3 - 1
        };

        foreach (var p in polys)
        {
            IFormattable f = p;
            var latex = f.ToString("L", null);
            int opens = latex.Count(c => c == '{');
            int closes = latex.Count(c => c == '}');
            Assert.Equal(opens, closes);
        }
    }

    [Fact]
    public void Latex_Rational_FracFormat()
    {
        IFormattable r = Rational.Create((Integer)3, (Integer)4);
        var latex = r.ToString("L", null);
        Assert.Contains(@"\frac{", latex);
        int opens = latex.Count(c => c == '{');
        int closes = latex.Count(c => c == '}');
        Assert.Equal(opens, closes);
    }

    [Fact]
    public void Latex_Matrix_PmatrixFormat()
    {
        var m = Matrix<Integer>.Identity(3);
        IFormattable f = m;
        var latex = f.ToString("L", null);
        Assert.StartsWith(@"\begin{pmatrix}", latex);
        Assert.EndsWith(@"\end{pmatrix}", latex);
    }
}
