using Helium.Validated;

namespace Helium.Validated.Tests;

public class IntervalMathTests
{
    [Fact]
    public void Exp_Point_ContainsTruth()
    {
        var result = IntervalMath.Exp(Interval.Point(0.0));
        Assert.True(result.Contains(1.0));
    }

    [Fact]
    public void Log_Point_ContainsTruth()
    {
        var result = IntervalMath.Log(Interval.Point(Math.E));
        Assert.True(result.Contains(1.0));
    }

    [Fact]
    public void Log_NonPositive_Throws()
    {
        Assert.Throws<ArithmeticException>(() => IntervalMath.Log(new Interval(-1.0, 2.0)));
    }

    [Fact]
    public void Sin_Point_ContainsTruth()
    {
        var result = IntervalMath.Sin(Interval.Point(Math.PI / 2.0));
        Assert.True(result.Contains(1.0));
    }

    [Fact]
    public void Cos_Point_ContainsTruth()
    {
        var result = IntervalMath.Cos(Interval.Point(0.0));
        Assert.True(result.Contains(1.0));
    }

    [Fact]
    public void Sqrt_Point_ContainsTruth()
    {
        var result = IntervalMath.Sqrt(Interval.Point(4.0));
        Assert.True(result.Contains(2.0));
    }

    [Fact]
    public void Sqrt_Negative_Throws()
    {
        Assert.Throws<ArithmeticException>(() => IntervalMath.Sqrt(new Interval(-1.0, 4.0)));
    }
}
