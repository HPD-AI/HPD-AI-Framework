using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;

namespace Helium.Algorithms.Tests;

public class SummationTests
{
    private static SparsePolynomial<Rational> P(string s) => SparsePolynomial<Rational>.Parse(s);

    [Fact]
    public void Gosper_FindsPolynomialAntidifference_ForOddLinearTerm()
    {
        var term = RationalFunctionField.Of(P("2x + 1"), SparsePolynomial<Rational>.One);

        var certificate = Summation.Gosper.FindRationalAntidifference(term);

        Assert.NotNull(certificate);
        Assert.Equal(term, Summation.Gosper.Shift(certificate.Value, Rational.One) - certificate.Value);
        Assert.Equal(RationalFunctionField.Of(P("x^2"), SparsePolynomial<Rational>.One), certificate.Value);
    }

    [Fact]
    public void Gosper_FindsPolynomialAntidifference_ForLinearTerm()
    {
        var term = RationalFunctionField.Of(P("x"), SparsePolynomial<Rational>.One);

        var certificate = Summation.Gosper.FindRationalAntidifference(term);

        Assert.NotNull(certificate);
        Assert.Equal(term, Summation.Gosper.Shift(certificate.Value, Rational.One) - certificate.Value);
        Assert.Equal(RationalFunctionField.Of(P("1/2x^2 - 1/2x"), SparsePolynomial<Rational>.One), certificate.Value);
    }

    [Fact]
    public void Gosper_FindsRationalTelescoper_ForReciprocalProduct()
    {
        var term = RationalFunctionField.Of(SparsePolynomial<Rational>.One, P("x^2 + x"));

        var certificate = Summation.Gosper.FindRationalAntidifference(term);

        Assert.NotNull(certificate);
        Assert.Equal(term, Summation.Gosper.Shift(certificate.Value, Rational.One) - certificate.Value);
        Assert.Equal(RationalFunctionField.Of(P("-1"), P("x")), certificate.Value);
    }

    [Fact]
    public void Gosper_ReturnsNull_WhenBoundDoesNotReachPolynomialDegree()
    {
        var term = RationalFunctionField.Of(P("3x^2 + 3x + 1"), SparsePolynomial<Rational>.One);

        var certificate = Summation.Gosper.FindRationalAntidifference(term, maxNumeratorDegree: 2);

        Assert.Null(certificate);
    }

    [Fact]
    public void ShiftPolynomial_SubstitutesXPlusOneExactly()
    {
        var shifted = Summation.Gosper.Shift(P("x^2 - x"), Rational.One);

        Assert.Equal(P("x^2 + x"), shifted);
    }
}
