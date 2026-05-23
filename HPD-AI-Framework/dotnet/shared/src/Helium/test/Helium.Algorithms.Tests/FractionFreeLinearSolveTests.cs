using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;

namespace Helium.Algorithms.Tests;

public class FractionFreeLinearSolveTests
{
    [Fact]
    public void Solve_IntegerSystem_ReturnsExactRationalSolution()
    {
        var a = Matrix<Integer>.FromArray(2, 2, [
            (Integer)2, (Integer)1,
            (Integer)1, (Integer)3]);
        var b = Vector<Integer>.FromArray((Integer)5, (Integer)7);

        var x = FractionFreeLinearSolve.Solve(a, b);

        Assert.NotNull(x);
        Assert.Equal(Rational.Create((Integer)8, (Integer)5), x.Value[0]);
        Assert.Equal(Rational.Create((Integer)9, (Integer)5), x.Value[1]);
    }

    [Fact]
    public void SolveCramer_GenericEuclideanDomain_ReturnsNumeratorsAndDenominator()
    {
        var a = Matrix<Rational>.FromArray(2, 2, [
            (Rational)2, (Rational)1,
            (Rational)1, (Rational)3]);
        var b = Vector<Rational>.FromArray((Rational)5, (Rational)7);

        var solved = FractionFreeLinearSolve.SolveCramer(a, b);

        Assert.NotNull(solved);
        var (numerators, denominator) = solved.Value;
        Assert.Equal((Rational)5, denominator);
        Assert.Equal((Rational)8, numerators[0]);
        Assert.Equal((Rational)9, numerators[1]);
    }

    [Fact]
    public void Solve_SingularIntegerSystem_ReturnsNull()
    {
        var a = Matrix<Integer>.FromArray(2, 2, [
            (Integer)1, (Integer)2,
            (Integer)2, (Integer)4]);
        var b = Vector<Integer>.FromArray((Integer)1, (Integer)3);

        var x = FractionFreeLinearSolve.Solve(a, b);

        Assert.Null(x);
    }

    [Fact]
    public void Inverse_IntegerMatrix_ReturnsExactRationalInverse()
    {
        var a = Matrix<Integer>.FromArray(2, 2, [
            (Integer)2, (Integer)1,
            (Integer)1, (Integer)3]);

        var inverse = FractionFreeLinearSolve.Inverse(a);

        Assert.NotNull(inverse);
        Assert.Equal(Rational.Create((Integer)3, (Integer)5), inverse.Value[0, 0]);
        Assert.Equal(Rational.Create((Integer)(-1), (Integer)5), inverse.Value[0, 1]);
        Assert.Equal(Rational.Create((Integer)(-1), (Integer)5), inverse.Value[1, 0]);
        Assert.Equal(Rational.Create((Integer)2, (Integer)5), inverse.Value[1, 1]);
    }

    [Fact]
    public void InverseAdjugate_GenericEuclideanDomain_ReturnsAdjugateAndDeterminant()
    {
        var a = Matrix<Rational>.FromArray(2, 2, [
            (Rational)2, (Rational)1,
            (Rational)1, (Rational)3]);

        var inverse = FractionFreeLinearSolve.InverseAdjugate(a);

        Assert.NotNull(inverse);
        var (adjugate, determinant) = inverse.Value;
        Assert.Equal((Rational)5, determinant);
        Assert.Equal((Rational)3, adjugate[0, 0]);
        Assert.Equal((Rational)(-1), adjugate[0, 1]);
        Assert.Equal((Rational)(-1), adjugate[1, 0]);
        Assert.Equal((Rational)2, adjugate[1, 1]);
    }

    [Fact]
    public void Inverse_MultipliesToIdentityOverRationals()
    {
        var a = Matrix<Integer>.FromArray(3, 3, [
            (Integer)2, (Integer)1, (Integer)0,
            (Integer)1, (Integer)3, (Integer)1,
            (Integer)0, (Integer)1, (Integer)2]);

        var inverse = FractionFreeLinearSolve.Inverse(a);
        Assert.NotNull(inverse);

        var rationalA = Matrix<Rational>.FromArray(3, 3, [
            (Rational)2, (Rational)1, (Rational)0,
            (Rational)1, (Rational)3, (Rational)1,
            (Rational)0, (Rational)1, (Rational)2]);

        Assert.Equal(Matrix<Rational>.Identity(3), rationalA * inverse.Value);
    }
}
