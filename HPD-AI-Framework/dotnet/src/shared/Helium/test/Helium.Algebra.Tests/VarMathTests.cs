using Helium.Primitives;
using Helium.Algebra;
using Double = Helium.Primitives.Double;

namespace Helium.Algebra.Tests;

public class VarMathTests
{
    private const double Tol = 1e-10;

    // Helper: compute f(x) and df/dx in one session
    private static (double Value, double Grad) Run(Func<Var<Double>, Var<Double>> f, double x)
    {
        using var session = Tape<Double>.Begin();
        var xv = new Var<Double>(new Double(x));
        var y = f(xv);
        var grads = session.Backward(y);
        double grad = xv.Index >= 0 ? (double)grads[xv.Index] : 0.0;
        return ((double)y.Value, grad);
    }

    // --- Exp ---

    [Fact]
    public void Exp_At0()
    {
        var (v, g) = Run(x => VarMath.Exp(x), 0.0);
        Assert.Equal(1.0, v, Tol);
        Assert.Equal(1.0, g, Tol);   // d/dx exp(x)|₀ = exp(0) = 1
    }

    [Fact]
    public void Exp_At1()
    {
        var (v, g) = Run(x => VarMath.Exp(x), 1.0);
        Assert.Equal(Math.E, v, Tol);
        Assert.Equal(Math.E, g, Tol);   // d/dx exp(x)|₁ = e
    }

    [Fact]
    public void Exp_AtNeg1()
    {
        var (v, g) = Run(x => VarMath.Exp(x), -1.0);
        Assert.Equal(1.0 / Math.E, v, Tol);
        Assert.Equal(1.0 / Math.E, g, Tol);
    }

    // --- Log ---

    [Fact]
    public void Log_At1()
    {
        var (v, g) = Run(x => VarMath.Log(x), 1.0);
        Assert.Equal(0.0, v, Tol);
        Assert.Equal(1.0, g, Tol);   // d/dx ln(x)|₁ = 1
    }

    [Fact]
    public void Log_At2()
    {
        var (v, g) = Run(x => VarMath.Log(x), 2.0);
        Assert.Equal(Math.Log(2.0), v, Tol);
        Assert.Equal(0.5, g, Tol);   // 1/2
    }

    [Fact]
    public void Log_AtE()
    {
        var (v, g) = Run(x => VarMath.Log(x), Math.E);
        Assert.Equal(1.0, v, Tol);
        Assert.Equal(1.0 / Math.E, g, Tol);
    }

    // --- Sin ---

    [Fact]
    public void Sin_At0()
    {
        var (v, g) = Run(x => VarMath.Sin(x), 0.0);
        Assert.Equal(0.0, v, Tol);
        Assert.Equal(1.0, g, Tol);   // cos(0) = 1
    }

    [Fact]
    public void Sin_AtPiOver2()
    {
        var (v, g) = Run(x => VarMath.Sin(x), Math.PI / 2.0);
        Assert.Equal(1.0, v, Tol);
        Assert.Equal(0.0, g, Tol);   // cos(π/2) ≈ 0
    }

    [Fact]
    public void Sin_AtPi()
    {
        var (v, g) = Run(x => VarMath.Sin(x), Math.PI);
        Assert.Equal(0.0, v, Tol);
        Assert.Equal(-1.0, g, Tol);  // cos(π) = -1
    }

    // --- Cos ---

    [Fact]
    public void Cos_At0()
    {
        var (v, g) = Run(x => VarMath.Cos(x), 0.0);
        Assert.Equal(1.0, v, Tol);
        Assert.Equal(0.0, g, Tol);   // -sin(0) = 0
    }

    [Fact]
    public void Cos_AtPiOver2()
    {
        var (v, g) = Run(x => VarMath.Cos(x), Math.PI / 2.0);
        Assert.Equal(0.0, v, Tol);
        Assert.Equal(-1.0, g, Tol);  // -sin(π/2) = -1
    }

    // --- Sqrt ---

    [Fact]
    public void Sqrt_At4()
    {
        var (v, g) = Run(x => VarMath.Sqrt(x), 4.0);
        Assert.Equal(2.0, v, Tol);
        Assert.Equal(0.25, g, Tol);  // 1/(2√4) = 1/4
    }

    [Fact]
    public void Sqrt_At1()
    {
        var (v, g) = Run(x => VarMath.Sqrt(x), 1.0);
        Assert.Equal(1.0, v, Tol);
        Assert.Equal(0.5, g, Tol);   // 1/(2√1) = 1/2
    }

    [Fact]
    public void Sqrt_At0_TotalFunction()
    {
        // Gradient at 0 is 0 by convention (avoids +∞)
        var (v, g) = Run(x => VarMath.Sqrt(x), 0.0);
        Assert.Equal(0.0, v, Tol);
        Assert.Equal(0.0, g, Tol);
    }

    // --- Chain rule ---

    [Fact]
    public void Chain_ExpLog_GradIsOne()
    {
        // d/dx exp(ln(x)) = exp(ln(x)) · (1/x) = x/x = 1
        var (v, g) = Run(x => VarMath.Exp(VarMath.Log(x)), 2.0);
        Assert.Equal(2.0, v, Tol);
        Assert.Equal(1.0, g, Tol);
    }

    [Fact]
    public void Chain_Pythagorean_ValueIsOne()
    {
        // sin²(x) + cos²(x) = 1, gradient = 0
        var (v, g) = Run(x => VarMath.Sin(x) * VarMath.Sin(x) +
                               VarMath.Cos(x) * VarMath.Cos(x), 1.0);
        Assert.Equal(1.0, v, Tol);
        Assert.Equal(0.0, g, Tol);
    }

    // --- No tape: creates constant node ---

    [Fact]
    public void NoTape_Exp_IsConstant()
    {
        // Outside a session, Exp returns a constant (Index = -1)
        var result = VarMath.Exp(new Var<Double>(new Double(1.0)));
        Assert.Equal(-1, result.Index);
        Assert.Equal(Math.E, (double)result.Value, Tol);
    }
}
