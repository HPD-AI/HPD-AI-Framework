using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class VarTests
{
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);
    private static T VarFromInt<T>(int n) where T : IRing<T> => T.FromInt(n);

    // =========================================================================
    // Primal values (no tape needed)
    // =========================================================================

    [Fact]
    public void Primal_Add() =>
        Assert.Equal(R(5), (new Var<Rational>(R(2)) + new Var<Rational>(R(3))).Value);

    [Fact]
    public void Primal_Sub() =>
        Assert.Equal(R(3), (new Var<Rational>(R(5)) - new Var<Rational>(R(2))).Value);

    [Fact]
    public void Primal_Mul() =>
        Assert.Equal(R(12), (new Var<Rational>(R(3)) * new Var<Rational>(R(4))).Value);

    [Fact]
    public void Primal_Negate() =>
        Assert.Equal(R(-7), (-new Var<Rational>(R(7))).Value);

    [Fact]
    public void Primal_Invert() =>
        Assert.Equal(R(1, 4), Var<Rational>.Invert(new Var<Rational>(R(4))).Value);

    [Fact]
    public void Primal_Div() =>
        Assert.Equal(R(3), (new Var<Rational>(R(6)) / new Var<Rational>(R(2))).Value);

    [Fact]
    public void Primal_Invert_Zero() =>
        Assert.Equal(R(0), Var<Rational>.Invert(new Var<Rational>(R(0))).Value); // total function

    [Fact]
    public void Primal_AdditiveIdentity() =>
        Assert.Equal(R(0), Var<Rational>.AdditiveIdentity.Value);

    [Fact]
    public void Primal_MultiplicativeIdentity() =>
        Assert.Equal(R(1), Var<Rational>.MultiplicativeIdentity.Value);

    [Fact]
    public void Primal_FromInt() =>
        Assert.Equal(R(5), VarFromInt<Var<Rational>>(5).Value);

    // =========================================================================
    // Value-based equality (tape index ignored)
    // =========================================================================

    [Fact]
    public void Equality_SameValue_DifferentIndices()
    {
        using var session = Tape<Rational>.Begin();
        var a = new Var<Rational>(R(3));
        var b = new Var<Rational>(R(3));
        Assert.NotEqual(a.Index, b.Index); // different slots
        Assert.Equal(a, b);                // but equal by value
    }

    [Fact]
    public void Equality_DifferentValues() =>
        Assert.NotEqual(new Var<Rational>(R(3)), new Var<Rational>(R(4)));

    [Fact]
    public void IsZero_True() =>
        Assert.True(new Var<Rational>(R(0)).IsZero);

    [Fact]
    public void IsZero_False() =>
        Assert.False(new Var<Rational>(R(1)).IsZero);

    [Fact]
    public void IsZero_AdditiveIdentity() =>
        Assert.True(Var<Rational>.AdditiveIdentity.IsZero);

    [Fact]
    public void IsZero_MultiplicativeIdentity() =>
        Assert.False(Var<Rational>.MultiplicativeIdentity.IsZero);

    // =========================================================================
    // Gradients — scalar functions f : Rational → Rational
    // =========================================================================

    private T GradOf<T>(Func<Var<T>, Var<T>> f, T x) where T : IField<T>
    {
        using var session = Tape<T>.Begin();
        var xv = new Var<T>(x);
        var y = f(xv);
        var grads = session.Backward(y);
        return xv.Index >= 0 ? grads[xv.Index] : T.AdditiveIdentity;
    }

    [Fact]
    public void Grad_Identity() =>
        Assert.Equal(R(1), GradOf(x => x, R(5)));

    [Fact]
    public void Grad_Negate() =>
        Assert.Equal(R(-1), GradOf(x => -x, R(5)));

    [Fact]
    public void Grad_Scale_By3() =>
        Assert.Equal(R(3), GradOf(x => new Var<Rational>(R(3)) * x, R(2)));

    [Fact]
    public void Grad_Square() =>
        Assert.Equal(R(6), GradOf(x => x * x, R(3)));

    [Fact]
    public void Grad_Square_AtZero() =>
        Assert.Equal(R(0), GradOf(x => x * x, R(0)));

    [Fact]
    public void Grad_Cube() =>
        // f(x) = x³, f'(2) = 3·4 = 12
        Assert.Equal(R(12), GradOf(x => x * x * x, R(2)));

    [Fact]
    public void Grad_Fourth()
    {
        // f(x) = x⁴, f'(2) = 4·8 = 32
        var g = GradOf(x => x * x * x * x, R(2));
        Assert.Equal(R(32), g);
    }

    [Fact]
    public void Grad_Invert()
    {
        // f(x) = 1/x, f'(2) = -1/4
        var g = GradOf(x => Var<Rational>.Invert(x), R(2));
        Assert.Equal(R(-1, 4), g);
    }

    [Fact]
    public void Grad_Invert_Zero()
    {
        // Invert(0) = 0 (total function), gradient = 0
        var g = GradOf(x => Var<Rational>.Invert(x), R(0));
        Assert.Equal(R(0), g);
    }

    [Fact]
    public void Grad_Div()
    {
        // f(x) = x/4, f'(3) = 1/4
        var g = GradOf(x => x / new Var<Rational>(R(4)), R(3));
        Assert.Equal(R(1, 4), g);
    }

    [Fact]
    public void Grad_SumAndSquare()
    {
        // f(x) = x² + x, f'(3) = 7
        var g = GradOf(x => x * x + x, R(3));
        Assert.Equal(R(7), g);
    }

    [Fact]
    public void Grad_SubtractLinear()
    {
        // f(x) = x² - 2x, f'(5) = 8
        var g = GradOf(x => x * x - new Var<Rational>(R(2)) * x, R(5));
        Assert.Equal(R(8), g);
    }

    [Fact]
    public void Grad_XPlusOne_Squared()
    {
        // f(x) = (x+1)², f'(x) = 2(x+1), f'(3) = 8
        var g = GradOf(x => (x + Var<Rational>.MultiplicativeIdentity) *
                             (x + Var<Rational>.MultiplicativeIdentity), R(3));
        Assert.Equal(R(8), g);
    }

    // =========================================================================
    // Fan-out: same variable used multiple times → gradients accumulate
    // =========================================================================

    [Fact]
    public void Grad_FanOut_XPlusX()
    {
        // f(x) = x + x = 2x, f'(5) = 2
        var g = GradOf(x => x + x, R(5));
        Assert.Equal(R(2), g);
    }

    [Fact]
    public void Grad_FanOut_XTimesX()
    {
        // f(x) = x*x = x², f'(3) = 6 (both branches accumulate)
        var g = GradOf(x => x * x, R(3));
        Assert.Equal(R(6), g);
    }

    [Fact]
    public void Grad_FanOut_Complex()
    {
        // f(x) = x² + x³, f'(2) = 4 + 12 = 16
        var g = GradOf(x => x * x + x * x * x, R(2));
        Assert.Equal(R(16), g);
    }

    // =========================================================================
    // Two-variable gradients
    // =========================================================================

    private (T Gx, T Gy) Grad2<T>(Func<Var<T>, Var<T>, Var<T>> f, T xi, T yi) where T : IField<T>
    {
        using var session = Tape<T>.Begin();
        var x = new Var<T>(xi);
        var y = new Var<T>(yi);
        var z = f(x, y);
        var grads = session.Backward(z);
        var gx = x.Index >= 0 ? grads[x.Index] : T.AdditiveIdentity;
        var gy = y.Index >= 0 ? grads[y.Index] : T.AdditiveIdentity;
        return (gx, gy);
    }

    [Fact]
    public void Grad2_AddXY()
    {
        var (gx, gy) = Grad2<Rational>((x, y) => x + y, R(2), R(3));
        Assert.Equal(R(1), gx);
        Assert.Equal(R(1), gy);
    }

    [Fact]
    public void Grad2_SubXY()
    {
        var (gx, gy) = Grad2<Rational>((x, y) => x - y, R(2), R(3));
        Assert.Equal(R(1), gx);
        Assert.Equal(R(-1), gy);
    }

    [Fact]
    public void Grad2_MulXY()
    {
        // f(x,y)=xy, ∂/∂x=y=3, ∂/∂y=x=2
        var (gx, gy) = Grad2<Rational>((x, y) => x * y, R(2), R(3));
        Assert.Equal(R(3), gx);
        Assert.Equal(R(2), gy);
    }

    [Fact]
    public void Grad2_XSqPlusY()
    {
        // f(x,y)=x²+y, ∂/∂x=2x=6, ∂/∂y=1
        var (gx, gy) = Grad2<Rational>((x, y) => x * x + y, R(3), R(1));
        Assert.Equal(R(6), gx);
        Assert.Equal(R(1), gy);
    }

    [Fact]
    public void Grad2_XSqTimesY()
    {
        // f(x,y)=x²y, ∂/∂x=2xy=12, ∂/∂y=x²=9
        var (gx, gy) = Grad2<Rational>((x, y) => x * x * y, R(3), R(2));
        Assert.Equal(R(12), gx);
        Assert.Equal(R(9), gy);
    }

    [Fact]
    public void Grad2_DivXY()
    {
        // f(x,y)=x/y at (6,2): ∂/∂x=1/y=1/2, ∂/∂y=-x/y²=-6/4=-3/2
        var (gx, gy) = Grad2<Rational>((x, y) => x / y, R(6), R(2));
        Assert.Equal(R(1, 2), gx);
        Assert.Equal(R(-3, 2), gy);
    }

    [Fact]
    public void Grad2_IndependentY_GetsZeroGrad()
    {
        // f(x,y)=x², y unused → gy=0
        var (gx, gy) = Grad2<Rational>((x, y) => x * x, R(3), R(1));
        Assert.Equal(R(6), gx);
        Assert.Equal(R(0), gy);
    }

    [Fact]
    public void Grad2_IndependentX_GetsZeroGrad()
    {
        // f(x,y)=y², x unused → gx=0
        var (gx, gy) = Grad2<Rational>((x, y) => y * y, R(2), R(4));
        Assert.Equal(R(0), gx);
        Assert.Equal(R(8), gy);
    }

    // =========================================================================
    // Constants carry zero gradient
    // =========================================================================

    [Fact]
    public void Constant_AddedToVar_GradUnchanged()
    {
        // f(x) = x + 5 (5 is constant), f'(x) = 1
        var g = GradOf(x => x + new Var<Rational>(R(5)), R(3));
        Assert.Equal(R(1), g);
    }

    [Fact]
    public void Constant_MultipliedByVar_GradIsConstant()
    {
        // f(x) = 5 * x, f'(x) = 5
        var g = GradOf(x => new Var<Rational>(R(5)) * x, R(3));
        Assert.Equal(R(5), g);
    }
}
