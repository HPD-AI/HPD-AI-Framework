using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class LinearSolveTests
{
    [Fact]
    public void SolveThenMultiply_GivesOriginalVector()
    {
        // A * solve(A, b) == b
        var A = Matrix<Rational>.FromArray(2, 2, [
            (Rational)2, (Rational)1,
            (Rational)1, (Rational)3]);
        var b = Vector<Rational>.FromArray((Rational)5, (Rational)7);
        var x = LinearSolve.Solve(A, b);
        Assert.NotNull(x);
        Assert.Equal(b, A * x.Value);
    }

    [Fact]
    public void SingularMatrix_ReturnsNull()
    {
        // Singular matrix (row 2 = 2 * row 1)
        var A = Matrix<Rational>.FromArray(2, 2, [
            (Rational)1, (Rational)2,
            (Rational)2, (Rational)4]);
        var b = Vector<Rational>.FromArray((Rational)1, (Rational)3);
        var x = LinearSolve.Solve(A, b);
        Assert.Null(x);
    }

    [Fact]
    public void IdentityMatrix_SolutionEqualsRhs()
    {
        var I = Matrix<Rational>.Identity(3);
        var b = Vector<Rational>.FromArray((Rational)1, (Rational)2, (Rational)3);
        var x = LinearSolve.Solve(I, b);
        Assert.NotNull(x);
        Assert.Equal(b, x.Value);
    }
}
