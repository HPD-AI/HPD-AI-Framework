using Helium.Validated;
using Helium.Validated.Autodiff;

namespace Helium.Validated.Tests;

public class ValidatedGradTests
{
    [Fact]
    public void Square_Point_ContainsDerivative()
    {
        var (value, grad) = ValidatedGrad.ValueAndGrad(x => x * x, Interval.Point(3.0));
        Assert.True(value.Contains(9.0));
        Assert.True(grad.Contains(6.0));
    }

    [Fact]
    public void Exp_Point_ContainsDerivative()
    {
        var (value, grad) = ValidatedGrad.ValueAndGrad(ValidatedVarMath.Exp, Interval.Point(0.0));
        Assert.True(value.Contains(1.0));
        Assert.True(grad.Contains(1.0));
    }

    [Fact]
    public void VectorInput_Product()
    {
        var (_, grad) = ValidatedGrad.ValueAndGrad(
            xs => xs[0] * xs[1],
            [Interval.Point(2.0), Interval.Point(3.0)]);

        Assert.True(grad[0].Contains(3.0));
        Assert.True(grad[1].Contains(2.0));
    }
}
