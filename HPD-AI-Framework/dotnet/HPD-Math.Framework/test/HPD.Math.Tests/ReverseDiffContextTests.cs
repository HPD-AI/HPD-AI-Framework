using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[ReverseDiffContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Nodes = 8)]
public readonly partial struct RationalReverseDiffContext
{
    partial void Build(ref Scope d)
    {
        var x = d.Input(new Rational32(2, 1));
        var y = d.Input(new Rational32(5, 1));

        d.Return((x * y) + x);
    }
}

public sealed class ReverseDiffContextTests
{
    [Fact]
    public void ReverseDiffContext_GeneratesHiddenFrameRunner()
    {
        var result = default(RationalReverseDiffContext.Result);
        var status = new RationalReverseDiffContext().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(new Rational32(12, 1), result.Primal);
        Assert.Equal(4, result.GradientCount);
        Assert.Equal(new Rational32(6, 1), result.GradientAt(0));
        Assert.Equal(new Rational32(2, 1), result.GradientAt(1));
    }
}
