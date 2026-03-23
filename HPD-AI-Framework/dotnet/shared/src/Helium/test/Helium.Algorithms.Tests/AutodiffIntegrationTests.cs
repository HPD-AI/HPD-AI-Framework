using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

/// <summary>
/// Integration tests verifying autodiff through existing algorithms without modification.
/// Var&lt;T&gt; : IField&lt;Var&lt;T&gt;&gt; lets gradient information flow through LinearSolve,
/// Determinant, and MatrixInverse via type substitution — no per-algorithm rules.
/// All tests use Rational for exact, zero-drift arithmetic.
/// </summary>
public class AutodiffIntegrationTests
{
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);

    // =========================================================================
    // Gradient through Determinant.ComputeOverField
    //
    // A = [[a,b],[c,d]] at a=1,b=2,c=3,d=4. det = ad-bc = 4-6 = -2.
    // ∂det/∂a = d = 4, ∂det/∂b = -c = -3,
    // ∂det/∂c = -b = -2, ∂det/∂d = a = 1.
    // =========================================================================

    [Fact]
    public void Det_Primal_ExactValue()
    {
        using var session = Tape<Rational>.Begin();
        var M = Matrix<Var<Rational>>.FromArray(2, 2, [
            new Var<Rational>(R(1)), new Var<Rational>(R(2)),
            new Var<Rational>(R(3)), new Var<Rational>(R(4))]);
        var det = Determinant.ComputeOverField(M);
        Assert.Equal(R(-2), det.Value);
    }

    [Fact]
    public void Det_Grad_AllEntries()
    {
        // Input: flattened [a,b,c,d], f = det([[a,b],[c,d]])
        var x0 = Vector<Rational>.FromArray(R(1), R(2), R(3), R(4));

        var grad = Grad.Of(v =>
        {
            var M = Matrix<Var<Rational>>.FromArray(2, 2, [v[0], v[1], v[2], v[3]]);
            return Determinant.ComputeOverField(M);
        }, x0);

        Assert.Equal(R(4),  grad[0]);   // ∂det/∂a = d
        Assert.Equal(R(-3), grad[1]);   // ∂det/∂b = -c
        Assert.Equal(R(-2), grad[2]);   // ∂det/∂c = -b
        Assert.Equal(R(1),  grad[3]);   // ∂det/∂d = a
    }

    // =========================================================================
    // Gradient through LinearSolve
    //
    // A = [[2,1],[1,3]] (fixed), b = [5,10] (variable).
    // x = A⁻¹b.  A⁻¹ = (1/5)[[3,-1],[-1,2]].
    // ∂x[i]/∂b[j] = (A⁻¹)[i,j].
    // =========================================================================

    [Fact]
    public void LinearSolve_Primal_Exact()
    {
        using var session = Tape<Rational>.Begin();
        var A = Matrix<Var<Rational>>.FromArray(2, 2, [
            new Var<Rational>(R(2)), new Var<Rational>(R(1)),
            new Var<Rational>(R(1)), new Var<Rational>(R(3))]);
        var b = Vector<Var<Rational>>.FromArray(
            new Var<Rational>(R(5)), new Var<Rational>(R(10)));
        var x = LinearSolve.Solve(A, b);
        Assert.NotNull(x);
        Assert.Equal(R(1), x.Value[0].Value);
        Assert.Equal(R(3), x.Value[1].Value);
    }

    [Fact]
    public void LinearSolve_Grad_dX0_db()
    {
        // ∂x[0]/∂b = first row of A⁻¹ = [3/5, -1/5]
        var b0 = Vector<Rational>.FromArray(R(5), R(10));

        var grad = Grad.Of(bv =>
        {
            // A has constant Var entries (no tape participation)
            var A = Matrix<Var<Rational>>.FromArray(2, 2, [
                new Var<Rational>(R(2)), new Var<Rational>(R(1)),
                new Var<Rational>(R(1)), new Var<Rational>(R(3))]);
            return LinearSolve.Solve(A, bv)!.Value[0];
        }, b0);

        Assert.Equal(R(3, 5),  grad[0]);
        Assert.Equal(R(-1, 5), grad[1]);
    }

    [Fact]
    public void LinearSolve_Grad_dX1_db()
    {
        // ∂x[1]/∂b = second row of A⁻¹ = [-1/5, 2/5]
        var b0 = Vector<Rational>.FromArray(R(5), R(10));

        var grad = Grad.Of(bv =>
        {
            var A = Matrix<Var<Rational>>.FromArray(2, 2, [
                new Var<Rational>(R(2)), new Var<Rational>(R(1)),
                new Var<Rational>(R(1)), new Var<Rational>(R(3))]);
            return LinearSolve.Solve(A, bv)!.Value[1];
        }, b0);

        Assert.Equal(R(-1, 5), grad[0]);
        Assert.Equal(R(2, 5),  grad[1]);
    }

    // =========================================================================
    // Gradient through MatrixInverse
    //
    // A = [[4,7],[2,6]], det = 10. A⁻¹ = (1/10)[[6,-7],[-2,4]].
    // =========================================================================

    [Fact]
    public void MatrixInverse_Primal_Exact()
    {
        using var _ = Tape<Rational>.Begin();
        var A = Matrix<Var<Rational>>.FromArray(2, 2, [
            new Var<Rational>(R(4)), new Var<Rational>(R(7)),
            new Var<Rational>(R(2)), new Var<Rational>(R(6))]);
        var inv = MatrixInverse.Compute(A);
        Assert.NotNull(inv);
        Assert.Equal(R(3, 5),   inv.Value[0, 0].Value);   // 6/10
        Assert.Equal(R(-7, 10), inv.Value[0, 1].Value);
        Assert.Equal(R(-1, 5),  inv.Value[1, 0].Value);   // -2/10
        Assert.Equal(R(2, 5),   inv.Value[1, 1].Value);   // 4/10
    }

    // =========================================================================
    // Exact gradient descent — zero floating point drift
    // =========================================================================

    [Fact]
    public void GradientDescent_OneStep_Exact()
    {
        // f(x) = x², ∇f = 2x. x₀ = 3, α = 1/6 → x₁ = 2 exactly.
        Rational x = R(3);
        Rational alpha = R(1, 6);
        Rational g = Grad.Scalar(xv => xv * xv, x);
        Assert.Equal(R(2), x - alpha * g);
    }

    [Fact]
    public void GradientDescent_ThreeSteps_ExactRational()
    {
        // f(x) = x², α = 1/6. Start x₀ = 6.
        // x₁ = 6 - 2 = 4, x₂ = 4 - 4/3 = 8/3, x₃ = 8/3 - 8/9 = 16/9.
        Rational x = R(6);
        Rational alpha = R(1, 6);
        for (int i = 0; i < 3; i++)
        {
            Rational g = Grad.Scalar(xv => xv * xv, x);
            x = x - alpha * g;
        }
        Assert.Equal(R(16, 9), x);
    }

    // =========================================================================
    // Newton's method over Rational
    // =========================================================================

    [Fact]
    public void Newton_OneStep_Rational()
    {
        // f(x) = x² - 2 from x₀ = 3/2.
        // f(x₀) = 1/4, f'(x₀) = 3. x₁ = 3/2 - 1/12 = 17/12.
        Rational x0 = R(3, 2);
        Rational fx0 = x0 * x0 - R(2);
        Rational dfx0 = ForwardDiff.Diff<Rational>(x => x * x, x0);
        Rational x1 = x0 - fx0 / dfx0;
        Assert.Equal(R(17, 12), x1);
    }

    [Fact]
    public void Newton_TwoSteps_ExactRational()
    {
        // x₀=3/2, x₁=17/12, x₂=577/408.
        Rational x = R(3, 2);
        for (int i = 0; i < 2; i++)
        {
            Rational fx = x * x - R(2);
            Rational dfx = ForwardDiff.Diff<Rational>(xv => xv * xv, x);
            x -= fx / dfx;
        }
        Assert.Equal(R(577, 408), x);
    }

    // =========================================================================
    // Type substitution correctness checks
    // =========================================================================

    [Fact]
    public void VarField_LUDecompose_Succeeds()
    {
        using var session = Tape<Rational>.Begin();
        var A = Matrix<Var<Rational>>.FromArray(2, 2, [
            new Var<Rational>(R(2)), new Var<Rational>(R(1)),
            new Var<Rational>(R(1)), new Var<Rational>(R(3))]);
        var result = LUDecomposition.Decompose(A);
        Assert.NotNull(result);
        Assert.Equal(2, result.Value.L.Rows);
        Assert.Equal(2, result.Value.U.Cols);
    }

    [Fact]
    public void VarField_LinearSolve_PrimalMatchesDirect()
    {
        // Direct Rational solve
        var A = Matrix<Rational>.FromArray(2, 2, [R(2), R(1), R(1), R(3)]);
        var b = Vector<Rational>.FromArray(R(5), R(10));
        var xDirect = LinearSolve.Solve(A, b)!.Value;

        // Constant Var solve (no tape)
        var Av = Matrix<Var<Rational>>.FromArray(2, 2, [
            new Var<Rational>(R(2)), new Var<Rational>(R(1)),
            new Var<Rational>(R(1)), new Var<Rational>(R(3))]);
        var bv = Vector<Var<Rational>>.FromArray(
            new Var<Rational>(R(5)), new Var<Rational>(R(10)));
        var xVar = LinearSolve.Solve(Av, bv)!.Value;

        Assert.Equal(xDirect[0], xVar[0].Value);
        Assert.Equal(xDirect[1], xVar[1].Value);
    }

    [Fact]
    public void VarField_Det_PrimalMatchesDirect()
    {
        var A = Matrix<Rational>.FromArray(2, 2, [R(1), R(2), R(3), R(4)]);
        var detDirect = Determinant.ComputeOverField(A);

        var Av = Matrix<Var<Rational>>.FromArray(2, 2, [
            new Var<Rational>(R(1)), new Var<Rational>(R(2)),
            new Var<Rational>(R(3)), new Var<Rational>(R(4))]);
        var detVar = Determinant.ComputeOverField(Av);

        Assert.Equal(detDirect, detVar.Value);
    }

    // =========================================================================
    // B1: Symbolic zero propagation
    // Constants combined inside a tape session must not allocate tape slots.
    // =========================================================================

    [Fact]
    public void B1_ConstantOps_NoTapeSlots()
    {
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(3));   // input, gets slot 0

        // All-constant subexpressions: no slots allocated, Index == -1
        var c1 = Var<Rational>.Constant(R(2));
        var c2 = Var<Rational>.Constant(R(5));
        var sum = c1 + c2;        // both constant → no slot
        var prod = c1 * c2;       // both constant → no slot
        var neg = -c1;             // constant → no slot

        Assert.Equal(-1, sum.Index);
        Assert.Equal(-1, prod.Index);
        Assert.Equal(-1, neg.Index);

        // x + c1 must still allocate (one active input)
        var mixed = x + c1;
        Assert.True(mixed.Index >= 0);
    }

    [Fact]
    public void B1_ConstantInvert_NoTapeSlot()
    {
        using var session = Tape<Rational>.Begin();
        var c = Var<Rational>.Constant(R(2));
        var inv = Var<Rational>.Invert(c);
        Assert.Equal(-1, inv.Index);
        Assert.Equal(R(1, 2), inv.Value);
    }

    // =========================================================================
    // B2: ValueAndGrad
    // =========================================================================

    [Fact]
    public void B2_ValueAndGrad_Scalar()
    {
        // f(x) = x^3 at x=2: value=8, grad=3x²=12
        var (value, grad) = Grad.ValueAndGrad<Rational>(x => x * x * x, R(2));
        Assert.Equal(R(8), value);
        Assert.Equal(R(12), grad);
    }

    [Fact]
    public void B2_ValueAndGrad_ExtensionMember()
    {
        Func<Var<Rational>, Var<Rational>> f = x => x * x * x;
        var fn = f.ValueAndGrad();
        var (value, grad) = fn(R(3));
        Assert.Equal(R(27), value);
        Assert.Equal(R(27), grad);   // 3*(3²)=27
    }

    [Fact]
    public void B2_ValueAndGrad_Vector()
    {
        // f(x,y) = x*y + x at (2,3): value=8, ∂f/∂x=4, ∂f/∂y=2
        var x0 = Vector<Rational>.FromArray(R(2), R(3));
        var (value, grad) = Grad.ValueAndGrad<Rational>(
            v => v[0] * v[1] + v[0], x0);
        Assert.Equal(R(8), value);
        Assert.Equal(R(4), grad[0]);  // y+1=4
        Assert.Equal(R(2), grad[1]);  // x=2
    }

    // =========================================================================
    // B3: CustomVjp
    // =========================================================================

    [Fact]
    public void B3_CustomVjp_PassesPrimalValue()
    {
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(5));
        // Custom node: value = x*2, backward: cotangent passes through scaled by 2
        var y = Grad.CustomVjp(R(10), [x], g => [g * R(2)]);
        Assert.Equal(R(10), y.Value);
        Assert.True(y.Index >= 0);
    }

    [Fact]
    public void B3_CustomVjp_BackwardRule()
    {
        // f(x) = x*2 with custom VJP. grad = 2.
        Rational grad;
        {
            using var session = Tape<Rational>.Begin();
            var x = new Var<Rational>(R(5));
            var y = Grad.CustomVjp(x.Value * R(2), [x], g => [g * R(2)]);
            var grads = session.Backward(y);
            grad = grads[x.Index];
        }
        Assert.Equal(R(2), grad);
    }

    [Fact]
    public void B3_CustomVjp_NoActiveInputs_ReturnsConstant()
    {
        using var session = Tape<Rational>.Begin();
        var c = Var<Rational>.Constant(R(5));
        var y = Grad.CustomVjp(R(10), [c], g => [g]);
        Assert.Equal(-1, y.Index);
    }
}
