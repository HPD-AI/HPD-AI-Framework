using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;
using Double = Helium.Primitives.Double;

namespace Helium.Algorithms.Tests;

public class GradTests
{
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);

    // =========================================================================
    // Grad.Scalar
    // =========================================================================

    [Fact]
    public void Scalar_Identity() =>
        Assert.Equal(R(1), Grad.Scalar(x => x, R(7)));

    [Fact]
    public void Scalar_Constant() =>
        Assert.Equal(R(0), Grad.Scalar(x => Var<Rational>.AdditiveIdentity, R(5)));

    [Fact]
    public void Scalar_Scale() =>
        Assert.Equal(R(3), Grad.Scalar(x => new Var<Rational>(R(3)) * x, R(2)));

    [Fact]
    public void Scalar_Square() =>
        Assert.Equal(R(6), Grad.Scalar(x => x * x, R(3)));

    [Fact]
    public void Scalar_Cube() =>
        Assert.Equal(R(12), Grad.Scalar(x => x * x * x, R(2)));

    [Fact]
    public void Scalar_Invert() =>
        // f(x)=1/x, f'(2)=-1/4
        Assert.Equal(R(-1, 4), Grad.Scalar(x => Var<Rational>.Invert(x), R(2)));

    [Fact]
    public void Scalar_InvertZero_TotalFunction() =>
        Assert.Equal(R(0), Grad.Scalar(x => Var<Rational>.Invert(x), R(0)));

    [Fact]
    public void Scalar_Over_Double()
    {
        var g = Grad.Scalar(x => x * x, new Double(3.0));
        Assert.Equal(new Double(6.0), g);
    }

    // =========================================================================
    // Grad.Of — gradient of vector → scalar
    // =========================================================================

    private static Vector<Rational> V(params int[] vals) =>
        Vector<Rational>.FromArray(vals.Select(n => R(n)).ToArray());

    [Fact]
    public void Of_SumXY()
    {
        // f(x,y)=x+y, ∇f=(1,1)
        var g = Grad.Of(v => v[0] + v[1], V(2, 3));
        Assert.Equal(R(1), g[0]);
        Assert.Equal(R(1), g[1]);
    }

    [Fact]
    public void Of_DiffXY()
    {
        // f(x,y)=x-y, ∇f=(1,-1)
        var g = Grad.Of(v => v[0] - v[1], V(2, 3));
        Assert.Equal(R(1), g[0]);
        Assert.Equal(R(-1), g[1]);
    }

    [Fact]
    public void Of_ProductXY()
    {
        // f(x,y)=xy at (2,3): ∇f=(3,2)
        var g = Grad.Of(v => v[0] * v[1], V(2, 3));
        Assert.Equal(R(3), g[0]);
        Assert.Equal(R(2), g[1]);
    }

    [Fact]
    public void Of_XSqPlusYSq()
    {
        // f=x²+y² at (1,2): ∇f=(2,4)
        var g = Grad.Of(v => v[0] * v[0] + v[1] * v[1], V(1, 2));
        Assert.Equal(R(2), g[0]);
        Assert.Equal(R(4), g[1]);
    }

    [Fact]
    public void Of_XSqTimesY()
    {
        // f=x²y at (3,2): ∂f/∂x=2xy=12, ∂f/∂y=x²=9
        var g = Grad.Of(v => v[0] * v[0] * v[1], V(3, 2));
        Assert.Equal(R(12), g[0]);
        Assert.Equal(R(9), g[1]);
    }

    [Fact]
    public void Of_ThreeVars_XYZ()
    {
        // f=xyz at (2,3,4): ∇f=(12,8,6)
        var g = Grad.Of(v => v[0] * v[1] * v[2], V(2, 3, 4));
        Assert.Equal(R(12), g[0]);
        Assert.Equal(R(8), g[1]);
        Assert.Equal(R(6), g[2]);
    }

    [Fact]
    public void Of_SingleVar_MatchesScalar()
    {
        // Grad.Of with 1-vector should match Grad.Scalar
        var g = Grad.Of(v => v[0] * v[0], V(3));
        Assert.Equal(R(6), g[0]);
    }

    [Fact]
    public void Of_ResultLengthEqualsInputLength()
    {
        var g = Grad.Of(v => v[0] * v[1] * v[2], V(1, 2, 3));
        Assert.Equal(3, g.Length);
    }

    [Fact]
    public void Of_LinearCombination()
    {
        // f=2x+3y, ∇f=(2,3)
        var g = Grad.Of(v => new Var<Rational>(R(2)) * v[0] +
                             new Var<Rational>(R(3)) * v[1], V(1, 1));
        Assert.Equal(R(2), g[0]);
        Assert.Equal(R(3), g[1]);
    }

    // =========================================================================
    // Grad.Jacobian
    // =========================================================================

    private static Vector<Rational> VR(params Rational[] vals) =>
        Vector<Rational>.FromArray(vals);

    [Fact]
    public void Jacobian_Identity_2D()
    {
        // f(x,y)=(x,y), J=I
        var J = Grad.Jacobian(v => v, V(1, 2));
        var mat = J.ToMatrix();
        Assert.Equal(R(1), mat[0, 0]);
        Assert.Equal(R(0), mat[0, 1]);
        Assert.Equal(R(0), mat[1, 0]);
        Assert.Equal(R(1), mat[1, 1]);
    }

    [Fact]
    public void Jacobian_Scale_2D()
    {
        // f(x,y)=(2x,3y), J=diag(2,3)
        var J = Grad.Jacobian(
            v => Vector<Var<Rational>>.FromArray(
                new Var<Rational>(R(2)) * v[0],
                new Var<Rational>(R(3)) * v[1]),
            V(1, 1));
        var mat = J.ToMatrix();
        Assert.Equal(R(2), mat[0, 0]);
        Assert.Equal(R(0), mat[0, 1]);
        Assert.Equal(R(0), mat[1, 0]);
        Assert.Equal(R(3), mat[1, 1]);
    }

    [Fact]
    public void Jacobian_XSqYSq()
    {
        // f=(x²,y²) at (2,3): J=diag(4,6)
        var J = Grad.Jacobian(
            v => Vector<Var<Rational>>.FromArray(v[0] * v[0], v[1] * v[1]),
            V(2, 3));
        var mat = J.ToMatrix();
        Assert.Equal(R(4), mat[0, 0]);
        Assert.Equal(R(0), mat[0, 1]);
        Assert.Equal(R(0), mat[1, 0]);
        Assert.Equal(R(6), mat[1, 1]);
    }

    [Fact]
    public void Jacobian_Affine()
    {
        // f=(x+y, x-y), J=[[1,1],[1,-1]]
        var J = Grad.Jacobian(
            v => Vector<Var<Rational>>.FromArray(v[0] + v[1], v[0] - v[1]),
            V(5, 3));
        var mat = J.ToMatrix();
        Assert.Equal(R(1),  mat[0, 0]);
        Assert.Equal(R(1),  mat[0, 1]);
        Assert.Equal(R(1),  mat[1, 0]);
        Assert.Equal(R(-1), mat[1, 1]);
    }

    [Fact]
    public void Jacobian_XY_Cross()
    {
        // f=(xy, x+y) at (2,3): J=[[y,x],[1,1]]=[[3,2],[1,1]]
        var J = Grad.Jacobian(
            v => Vector<Var<Rational>>.FromArray(v[0] * v[1], v[0] + v[1]),
            V(2, 3));
        var mat = J.ToMatrix();
        Assert.Equal(R(3), mat[0, 0]);
        Assert.Equal(R(2), mat[0, 1]);
        Assert.Equal(R(1), mat[1, 0]);
        Assert.Equal(R(1), mat[1, 1]);
    }

    [Fact]
    public void Jacobian_DomainDim()
    {
        var J = Grad.Jacobian(v => v, V(1, 2, 3));
        Assert.Equal(3, J.DomainDim);
    }

    [Fact]
    public void Jacobian_CodomainDim()
    {
        var J = Grad.Jacobian(v => v, V(1, 2));
        Assert.Equal(2, J.CodomainDim);
    }

    // =========================================================================
    // Grad.HessianExact
    // =========================================================================

    // Helper to build a FPS-typed function from field operations
    // f : Vector<Var<FPS<T>>> -> Var<FPS<T>>

    [Fact]
    public void Hessian_1D_XSq()
    {
        // f(x)=x², H=[[2]]
        var x0 = Vector<Rational>.FromArray(R(5));
        var H = Grad.HessianExact<Rational>(v => v[0] * v[0], x0);
        var mat = H.GramMatrix;
        Assert.Equal(R(2), mat[0, 0]);
    }

    [Fact]
    public void Hessian_2D_XSqPlusYSq()
    {
        // f=x²+y², H=2I
        var x0 = Vector<Rational>.FromArray(R(1), R(2));
        var H = Grad.HessianExact<Rational>(
            v => v[0] * v[0] + v[1] * v[1], x0);
        var mat = H.GramMatrix;
        Assert.Equal(R(2), mat[0, 0]);
        Assert.Equal(R(0), mat[0, 1]);
        Assert.Equal(R(0), mat[1, 0]);
        Assert.Equal(R(2), mat[1, 1]);
    }

    [Fact]
    public void Hessian_2D_XTimesY()
    {
        // f=xy, H=[[0,1],[1,0]]
        var x0 = Vector<Rational>.FromArray(R(3), R(4));
        var H = Grad.HessianExact<Rational>(
            v => v[0] * v[1], x0);
        var mat = H.GramMatrix;
        Assert.Equal(R(0), mat[0, 0]);
        Assert.Equal(R(1), mat[0, 1]);
        Assert.Equal(R(1), mat[1, 0]);
        Assert.Equal(R(0), mat[1, 1]);
    }

    [Fact]
    public void Hessian_2D_XSqTimesY()
    {
        // f=x²y at (1,1): H=[[2y,2x],[2x,0]]=[[2,2],[2,0]]
        var x0 = Vector<Rational>.FromArray(R(1), R(1));
        var H = Grad.HessianExact<Rational>(
            v => v[0] * v[0] * v[1], x0);
        var mat = H.GramMatrix;
        Assert.Equal(R(2), mat[0, 0]);
        Assert.Equal(R(2), mat[0, 1]);
        Assert.Equal(R(2), mat[1, 0]);
        Assert.Equal(R(0), mat[1, 1]);
    }

    [Fact]
    public void Hessian_IsSymmetric()
    {
        // H of a smooth function is always symmetric
        var x0 = Vector<Rational>.FromArray(R(2), R(3));
        var H = Grad.HessianExact<Rational>(
            v => v[0] * v[0] * v[1] + v[1] * v[1], x0);
        Assert.True(H.IsSymmetric);
    }

    [Fact]
    public void Hessian_XPlusY_Squared()
    {
        // f=(x+y)²=x²+2xy+y², H=[[2,2],[2,2]]
        var x0 = Vector<Rational>.FromArray(R(0), R(0));
        var H = Grad.HessianExact<Rational>(
            v => (v[0] + v[1]) * (v[0] + v[1]), x0);
        var mat = H.GramMatrix;
        Assert.Equal(R(2), mat[0, 0]);
        Assert.Equal(R(2), mat[0, 1]);
        Assert.Equal(R(2), mat[1, 0]);
        Assert.Equal(R(2), mat[1, 1]);
    }

    // =========================================================================
    // Grad.Hessian throws NotSupportedException
    // =========================================================================

    [Fact]
    public void Hessian_Throws_NotSupported() =>
        Assert.Throws<NotSupportedException>(() =>
            Grad.Hessian(v => v[0] * v[0], V(1, 2)));

    // =========================================================================
    // ForwardDiff and Grad.Scalar agree (cross-validation)
    // =========================================================================

    [Fact]
    public void CrossVal_ForwardReverse_Agree()
    {
        // f(x) = x² + 3x, f'(4) = 8 + 3 = 11
        Rational xi = R(4);
        var forward = ForwardDiff.Diff<Rational>(
            x => x * x + FormalPowerSeries<Rational>.Constant(R(3)) * x, xi);
        var reverse = Grad.Scalar(x => x * x + new Var<Rational>(R(3)) * x, xi);
        Assert.Equal(forward, reverse);
        Assert.Equal(R(11), forward);
    }
}
