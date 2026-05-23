using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms.Tests;

/// <summary>
/// Integration tests for exact autodiff after the v2 algebra split.
/// Var&lt;T&gt; is reverse-mode ring autodiff, not a field wrapper for exact linear algebra.
/// </summary>
public class AutodiffIntegrationTests
{
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);

    [Fact]
    public void ReverseMode_PolynomialGradient_IsExact()
    {
        var grad = Grad.Scalar(x => x * x * x + new Var<Rational>(R(3)) * x, R(2));
        Assert.Equal(R(15), grad);
    }

    [Fact]
    public void ReverseMode_VectorGradient_IsExact()
    {
        var x0 = Vector<Rational>.FromArray(R(2), R(3), R(4));
        var grad = Grad.Of(v => v[0] * v[1] + v[2] * v[2], x0);
        Assert.Equal(R(3), grad[0]);
        Assert.Equal(R(2), grad[1]);
        Assert.Equal(R(8), grad[2]);
    }

    [Fact]
    public void ReverseMode_GradientDescentStep_HasNoFloatingPointDrift()
    {
        // f(x) = x^2, grad = 2x. x0 = 3, alpha = 1/6, x1 = 2 exactly.
        Rational x = R(3);
        Rational alpha = R(1, 6);
        Rational g = Grad.Scalar(xv => xv * xv, x);
        Assert.Equal(R(2), x - alpha * g);
    }

    [Fact]
    public void ForwardMode_NewtonStep_UsesExactFieldArithmetic()
    {
        // f(x) = x^2 - 2 from x0 = 3/2.
        // f(x0) = 1/4, f'(x0) = 3, x1 = 17/12.
        Rational x0 = R(3, 2);
        Rational fx0 = x0 * x0 - R(2);
        Rational dfx0 = ForwardDiff.Diff<Rational>(x => x * x, x0);
        Rational x1 = x0 - fx0 / dfx0;
        Assert.Equal(R(17, 12), x1);
    }

    [Fact]
    public void ReverseMode_CustomVjp_ComposesWithRingTape()
    {
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(3));
        var y = Grad.CustomVjp(R(9), [x], g => [g * R(6)]);
        var grads = session.Backward(y);
        Assert.Equal(R(6), grads[x.Index]);
    }

    [Fact]
    public void FieldOnlyVarInverse_RemainsAvailableForFieldScalars()
    {
        var grad = Grad.Scalar(x => Var<Rational>.Invert(x), R(2));
        Assert.Equal(R(-1, 4), grad);
    }
}
