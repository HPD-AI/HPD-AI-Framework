using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class TapeTests
{
    private static Rational R(int n) => (Rational)n;
    private static T VarFromInt<T>(int n) where T : IRing<T> => T.FromInt(n);

    // --- Index allocation ---

    [Fact]
    public void NoTape_Var_HasMinusOneIndex()
    {
        // Outside any session, new Var is a constant with Index = -1.
        var x = new Var<Rational>(R(3));
        Assert.Equal(-1, x.Index);
    }

    [Fact]
    public void Session_Var_HasNonNegativeIndex()
    {
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(3));
        Assert.True(x.Index >= 0);
    }

    [Fact]
    public void Session_TwoVars_HaveDistinctIndices()
    {
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(1));
        var y = new Var<Rational>(R(2));
        Assert.NotEqual(x.Index, y.Index);
    }

    // --- RAII / lifecycle ---

    [Fact]
    public void Session_DisposeClearsCurrent()
    {
        var session = Tape<Rational>.Begin();
        Assert.NotNull(Tape<Rational>.Current);
        session.Dispose();
        Assert.Null(Tape<Rational>.Current);
    }

    [Fact]
    public void Session_UsingPattern_ClearsCurrent()
    {
        using (var _ = Tape<Rational>.Begin())
        {
            Assert.NotNull(Tape<Rational>.Current);
        }
        Assert.Null(Tape<Rational>.Current);
    }

    [Fact]
    public void NestedSession_Throws()
    {
        using var outer = Tape<Rational>.Begin();
        Assert.Throws<InvalidOperationException>(() => Tape<Rational>.Begin());
    }

    // --- Backward pass ---

    [Fact]
    public void Backward_OutputIsInput_GradIsOne()
    {
        // f(x) = x, backward seeds output with 1, so df/dx = 1.
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(5));
        var grads = session.Backward(x);
        Assert.Equal(R(1), grads[x.Index]);
    }

    [Fact]
    public void Backward_UnusedVar_GradIsZero()
    {
        // y created but not used in computing f(x) = x.
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(3));
        var y = new Var<Rational>(R(7));
        var grads = session.Backward(x);
        Assert.Equal(R(1), grads[x.Index]);
        Assert.Equal(R(0), grads[y.Index]);
    }

    [Fact]
    public void Backward_GradsLength_CoversAllVars()
    {
        using var session = Tape<Rational>.Begin();
        var x = new Var<Rational>(R(1));
        var y = new Var<Rational>(R(2));
        var z = x + y;
        var grads = session.Backward(z);
        // Must have slots for x, y, z at minimum.
        Assert.True(grads.Length > Math.Max(x.Index, y.Index));
    }

    // --- Constants do not participate in gradient ---

    [Fact]
    public void Constant_HasMinusOneIndex()
    {
        using var _ = Tape<Rational>.Begin();
        var c = Var<Rational>.AdditiveIdentity;
        Assert.Equal(-1, c.Index);
    }

    [Fact]
    public void FromInt_IsConstant()
    {
        using var _ = Tape<Rational>.Begin();
        var c = VarFromInt<Var<Rational>>(5);
        Assert.Equal(-1, c.Index);
    }
}
