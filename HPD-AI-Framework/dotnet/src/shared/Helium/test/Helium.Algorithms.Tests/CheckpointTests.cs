using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

/// <summary>
/// Tests for Grad.Checkpoint — verifies that checkpointed segments produce
/// correct primals and gradients while keeping the outer tape small.
/// All tests use Rational for exact, zero-drift arithmetic.
/// </summary>
public class CheckpointTests
{
    private static Rational R(int n) => (Rational)n;

    // =========================================================================
    // Scalar form
    // =========================================================================

    [Fact]
    public void Checkpoint_ScalarSegment_CorrectPrimal()
    {
        // Checkpoint(x, v => v*v*v) at x=3 should give primal = 27.
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(3));
        var y = Grad.Checkpoint(x, v => v * v * v);
        Assert.Equal(R(27), y.Value);
    }

    [Fact]
    public void Checkpoint_ScalarSegment_CorrectGrad()
    {
        // d/dx x³ = 3x² = 27 at x=3.
        var grad = Grad.Scalar<Rational>(x => Grad.Checkpoint(x, v => v * v * v), R(3));
        Assert.Equal(R(27), grad);
    }

    [Fact]
    public void Checkpoint_DoesNotPolluteTape()
    {
        // Outer tape should have exactly 2 slots: one for x, one for the checkpoint output.
        // Without checkpointing, v*v*v would allocate 2 extra slots (two multiplies).
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(3));       // slot 0
        var y = Grad.Checkpoint(x, v => v * v * v);  // slot 1 (meta-closure only)
        Assert.Equal(0, x.Index);
        Assert.Equal(1, y.Index);
    }

    [Fact]
    public void Checkpoint_ComposedTwoSegments_CorrectGrad()
    {
        // f(x) = (x²)³ = x⁶. Two checkpointed segments: first squares, then cubes.
        // f'(x) = 6x⁵ = 6*2⁵ = 192 at x=2.
        var grad = Grad.Scalar<Rational>(x =>
        {
            var a = Grad.Checkpoint(x, v => v * v);
            return Grad.Checkpoint(a, v => v * v * v);
        }, R(2));
        Assert.Equal(R(192), grad);
    }

    [Fact]
    public void Checkpoint_GradMatchesFullTape()
    {
        // Verify checkpointed grad equals un-checkpointed grad for the same function.
        Func<Var<Rational>, Var<Rational>> f = v => v * v * v * v;  // x⁴

        var gradFull  = Grad.Scalar<Rational>(f, R(3));
        var gradCkpt  = Grad.Scalar<Rational>(x => Grad.Checkpoint(x, f), R(3));

        Assert.Equal(gradFull, gradCkpt);
    }

    [Fact]
    public void Checkpoint_NoActiveTape_RunsSegmentDirectly()
    {
        // With no tape active, Checkpoint just evaluates the segment.
        var result = Grad.Checkpoint(Var<Rational>.Constant(R(4)), v => v * v);
        Assert.Equal(R(16), result.Value);
    }

    [Fact]
    public void Checkpoint_ConstantInput_ReturnsConstant()
    {
        // Tape active, but input has Index = -1. CustomVjp short-circuits → constant output.
        using var session = Tape<Rational>.Begin();
        var c = Var<Rational>.Constant(R(5));
        var y = Grad.Checkpoint(c, v => v * v);
        Assert.Equal(-1, y.Index);
        Assert.Equal(R(25), y.Value);
    }

    [Fact]
    public void Checkpoint_ExactArithmetic_NoDrift()
    {
        // Multi-step loop over Rational: checkpointed result is exactly equal to
        // un-checkpointed result. No floating-point drift possible.
        Func<Var<Rational>, Var<Rational>> step = v => v * v + Var<Rational>.Constant(R(1));

        // Un-checkpointed
        var gradFull = Grad.Scalar<Rational>(x =>
        {
            var acc = x;
            for (int i = 0; i < 4; i++) acc = step(acc);
            return acc;
        }, R(2));

        // Checkpointed (each step is its own segment)
        var gradCkpt = Grad.Scalar<Rational>(x =>
        {
            var acc = x;
            for (int i = 0; i < 4; i++)
                acc = Grad.Checkpoint(acc, step);
            return acc;
        }, R(2));

        Assert.Equal(gradFull, gradCkpt);
    }

    // =========================================================================
    // Vector form
    // =========================================================================

    [Fact]
    public void Checkpoint_Vector_CorrectPrimal()
    {
        // segment(v) = v[0] * v[1] at (2, 3) → primal = 6.
        using var session = Tape<Rational>.Begin();
        var inputs = Vector<Var<Rational>>.FromArray(
            new Var<Rational>(R(2)), new Var<Rational>(R(3)));
        var y = Grad.Checkpoint(inputs, v => v[0] * v[1]);
        Assert.Equal(R(6), y.Value);
    }

    [Fact]
    public void Checkpoint_Vector_CorrectGrad()
    {
        // ∂/∂v[0] (v[0]*v[1]) = v[1] = 3, ∂/∂v[1] = v[0] = 2 at (2,3).
        var x0 = Vector<Rational>.FromArray(R(2), R(3));
        var grad = Grad.Of<Rational>(
            v => Grad.Checkpoint(v, vs => vs[0] * vs[1]), x0);
        Assert.Equal(R(3), grad[0]);
        Assert.Equal(R(2), grad[1]);
    }

    [Fact]
    public void Checkpoint_Vector_GradMatchesFullTape()
    {
        // Same function un-checkpointed vs checkpointed over a vector input.
        // f(v) = v[0]² + v[0]*v[1] + v[1]²
        Func<Vector<Var<Rational>>, Var<Rational>> f =
            v => v[0] * v[0] + v[0] * v[1] + v[1] * v[1];

        var x0 = Vector<Rational>.FromArray(R(3), R(5));

        var gradFull = Grad.Of<Rational>(f, x0);
        var gradCkpt = Grad.Of<Rational>(v => Grad.Checkpoint(v, f), x0);

        Assert.Equal(gradFull[0], gradCkpt[0]);
        Assert.Equal(gradFull[1], gradCkpt[1]);
    }
}
