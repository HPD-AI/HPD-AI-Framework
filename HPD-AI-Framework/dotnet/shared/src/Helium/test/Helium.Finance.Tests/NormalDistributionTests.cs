using Helium.Finance.Distributions;

namespace Helium.Finance.Tests;

public class NormalDistributionTests
{
    [Fact]
    public void PdfAtZeroMatchesReferenceValue()
    {
        AssertClose(0.3989422804014327, NormalDistribution.Pdf(0.0), 1e-16);
    }

    [Fact]
    public void PdfHandlesNonfiniteBoundariesExplicitly()
    {
        Assert.Equal(0.0, NormalDistribution.Pdf(double.PositiveInfinity));
        Assert.Equal(0.0, NormalDistribution.Pdf(double.NegativeInfinity));
        Assert.True(double.IsNaN(NormalDistribution.Pdf(double.NaN)));
    }

    [Theory]
    [InlineData(-3.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void PdfDerivativeMatchesAnalyticGaussianDerivative(double x)
    {
        var expected = -x * NormalDistribution.Pdf(x);

        AssertClose(expected, NormalDistribution.PdfDerivative(x), 1e-16);
    }

    [Fact]
    public void PdfDerivativeHandlesNonfiniteBoundariesExplicitly()
    {
        Assert.Equal(0.0, NormalDistribution.PdfDerivative(double.PositiveInfinity));
        Assert.Equal(0.0, NormalDistribution.PdfDerivative(double.NegativeInfinity));
        Assert.True(double.IsNaN(NormalDistribution.PdfDerivative(double.NaN)));
    }

    [Fact]
    public void CdfAtZeroIsOneHalf()
    {
        AssertClose(0.5, NormalDistribution.Cdf(0.0), 1e-16);
    }

    [Theory]
    [InlineData(-3.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void CdfDerivativeEqualsDensityLikeQuantLibCumulativeNormal(double x)
    {
        AssertClose(NormalDistribution.Pdf(x), NormalDistribution.CdfDerivative(x), 0.0);
    }

    [Fact]
    public void CdfPreservesFarTailProbability()
    {
        var lowerTail = NormalDistribution.Cdf(-8.0);
        var upperTail = NormalDistribution.Cdf(8.0);

        AssertClose(6.22096057427178e-16, lowerTail, 1e-28);
        Assert.True(lowerTail > 0.0);
        Assert.True(upperTail < 1.0);
        AssertClose(lowerTail, 1.0 - upperTail, 5e-17);
    }

    [Fact]
    public void InverseCdfAtOneHalfIsZero()
    {
        AssertClose(0.0, NormalDistribution.InverseCdf(0.5), 1e-14);
    }

    [Fact]
    public void InverseCdfReturnsFiniteTailValues()
    {
        var lowerTail = NormalDistribution.InverseCdf(double.Epsilon);
        var upperTail = NormalDistribution.InverseCdf(double.BitDecrement(1.0));

        Assert.True(double.IsFinite(lowerTail));
        Assert.True(double.IsFinite(upperTail));
        Assert.True(lowerTail < 0.0);
        Assert.True(upperTail > 0.0);
    }

    [Theory]
    [InlineData(-6.0)]
    [InlineData(-3.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(6.0)]
    public void InverseCdfRoundTripsCdf(double x)
    {
        var p = NormalDistribution.Cdf(x);
        AssertClose(x, NormalDistribution.InverseCdf(p), 1e-7);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    public void SymmetryIdentitiesHold(double x)
    {
        AssertClose(NormalDistribution.Pdf(x), NormalDistribution.Pdf(-x), 1e-16);
        AssertClose(1.0 - NormalDistribution.Cdf(x), NormalDistribution.Cdf(-x), 1e-15);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void InverseCdfRejectsInvalidProbabilities(double p)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NormalDistribution.InverseCdf(p));
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
