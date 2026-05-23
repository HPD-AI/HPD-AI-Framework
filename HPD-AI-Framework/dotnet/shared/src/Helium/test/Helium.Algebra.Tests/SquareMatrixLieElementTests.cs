using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class SquareMatrixLieElementTests
{
    private static Rational R(int n) => (Rational)n;

    private static SquareMatrixLieElement<Rational, Dim2> L(params Rational[] values) =>
        SquareMatrixLieElement<Rational, Dim2>.Create(Matrix<Rational>.FromArray(2, 2, values));

    [Fact]
    public void Create_RejectsNonSquareMatrix()
    {
        var matrix = Matrix<Rational>.FromArray(2, 3, [R(1), R(2), R(3), R(4), R(5), R(6)]);

        Assert.Throws<ArgumentException>(() => SquareMatrixLieElement<Rational, Dim2>.Create(matrix));
    }

    [Fact]
    public void Bracket_IsCommutator()
    {
        var x = L(R(1), R(2), R(3), R(4));
        var y = L(R(0), R(1), R(-1), R(2));

        Assert.Equal(x.Value * y.Value - y.Value * x.Value, SquareMatrixLieElement<Rational, Dim2>.Bracket(x, y).Value);
    }

    [Fact]
    public void Bracket_IsBilinear()
    {
        var x = L(R(1), R(2), R(3), R(4));
        var y = L(R(0), R(1), R(-1), R(2));
        var z = L(R(5), R(-2), R(7), R(3));
        var a = R(3);

        Assert.Equal(
            SquareMatrixLieElement<Rational, Dim2>.Bracket(x, z) + SquareMatrixLieElement<Rational, Dim2>.Bracket(y, z),
            SquareMatrixLieElement<Rational, Dim2>.Bracket(x + y, z));
        Assert.Equal(
            a * SquareMatrixLieElement<Rational, Dim2>.Bracket(x, y),
            SquareMatrixLieElement<Rational, Dim2>.Bracket(a * x, y));
    }

    [Fact]
    public void Bracket_IsAlternatingAndAntisymmetric()
    {
        var x = L(R(1), R(2), R(3), R(4));
        var y = L(R(0), R(1), R(-1), R(2));

        Assert.Equal(SquareMatrixLieElement<Rational, Dim2>.Zero, SquareMatrixLieElement<Rational, Dim2>.Bracket(x, x));
        Assert.Equal(
            -SquareMatrixLieElement<Rational, Dim2>.Bracket(y, x),
            SquareMatrixLieElement<Rational, Dim2>.Bracket(x, y));
    }

    [Fact]
    public void Bracket_SatisfiesJacobiIdentity()
    {
        var x = L(R(1), R(2), R(3), R(4));
        var y = L(R(0), R(1), R(-1), R(2));
        var z = L(R(5), R(-2), R(7), R(3));

        var jacobi =
            SquareMatrixLieElement<Rational, Dim2>.Bracket(x, SquareMatrixLieElement<Rational, Dim2>.Bracket(y, z)) +
            SquareMatrixLieElement<Rational, Dim2>.Bracket(y, SquareMatrixLieElement<Rational, Dim2>.Bracket(z, x)) +
            SquareMatrixLieElement<Rational, Dim2>.Bracket(z, SquareMatrixLieElement<Rational, Dim2>.Bracket(x, y));

        Assert.Equal(SquareMatrixLieElement<Rational, Dim2>.Zero, jacobi);
    }

    [Fact]
    public void Matrix_DoesNotImplementLieAlgebraDirectly()
    {
        var interfaces = typeof(Matrix<Rational>).GetInterfaces();

        Assert.DoesNotContain(interfaces, type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILieAlgebra<,>));
    }

    private readonly struct Dim2 : IFiniteDimension
    {
        public static int Value => 2;
    }
}
