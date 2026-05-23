using Helium.Primitives;
using Helium.Validated;

namespace Helium.Validated.Tests;

public class IntervalTests
{
    [Fact]
    public void DoesNotImplementExactAlgebraInterfaces()
    {
        var interfaces = typeof(Interval).GetInterfaces();
        Assert.DoesNotContain(interfaces, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRing<>));
        Assert.DoesNotContain(interfaces, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IField<>));
    }

    [Fact]
    public void Constructor_SwapsEndpoints()
    {
        var x = new Interval(3.0, 1.0);
        Assert.Equal(1.0, x.Lo);
        Assert.Equal(3.0, x.Hi);
    }

    [Fact]
    public void Point_IsPoint()
    {
        var x = Interval.Point(2.0);
        Assert.True(x.IsPoint);
        Assert.True(x.Contains(2.0));
    }

    [Fact]
    public void Add_PointIntervals_ContainsTrueResult()
    {
        var result = Interval.Point(0.1) + Interval.Point(0.2);
        Assert.True(result.Contains(0.3));
    }

    [Fact]
    public void Sub_SameInterval_DocumentsDependencyProblem()
    {
        var x = new Interval(1.0, 3.0);
        var result = x - x;
        Assert.True(result.Contains(0.0));
        Assert.False(result.IsPoint);
    }

    [Fact]
    public void Mul_MixedSigns()
    {
        var result = new Interval(-2.0, -1.0) * new Interval(3.0, 4.0);
        Assert.True(result.Contains(-8.0));
        Assert.True(result.Contains(-3.0));
    }

    [Fact]
    public void Divide_ContainsZero_Throws()
    {
        Assert.Throws<ArithmeticException>(() =>
            Interval.Divide(Interval.Point(1.0), new Interval(-1.0, 1.0)));
    }

    [Fact]
    public void Divide_NonzeroInterval_ContainsSampleResult()
    {
        var result = Interval.Divide(new Interval(4.0, 6.0), new Interval(2.0, 3.0));
        Assert.True(result.Contains(2.0));
    }

    [Fact]
    public void Overlaps()
    {
        Assert.True(new Interval(1.0, 3.0).Overlaps(new Interval(2.0, 4.0)));
        Assert.False(new Interval(1.0, 2.0).Overlaps(new Interval(3.0, 4.0)));
    }
}
