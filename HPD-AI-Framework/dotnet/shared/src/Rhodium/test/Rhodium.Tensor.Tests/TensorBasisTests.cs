using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

public class TensorBasisTests
{
    [Fact]
    public void TensorBasis_ConstructorSetsProperties()
    {
        var basis = new TensorBasis(10, 5);

        Assert.Equal(10, basis.AssetDimension);
        Assert.Equal(5, basis.VariantDimension);
        Assert.Equal(50, basis.Rank);
    }

    [Fact]
    public void TensorBasis_ToLinearMapsCorrectly()
    {
        var basis = new TensorBasis(10, 5);

        Assert.Equal(0, basis.ToLinear(0, 0));   // First element
        Assert.Equal(1, basis.ToLinear(0, 1));   // Second variant of first asset
        Assert.Equal(5, basis.ToLinear(1, 0));   // First variant of second asset
        Assert.Equal(49, basis.ToLinear(9, 4));  // Last element
    }

    [Theory]
    [InlineData(3, 4, 0, 0, 0)]
    [InlineData(3, 4, 0, 1, 1)]
    [InlineData(3, 4, 1, 0, 4)]
    [InlineData(3, 4, 2, 3, 11)]
    public void TensorBasis_ToLinearFormula(int assetDim, int variantDim, int assetIdx, int variantIdx, int expected)
    {
        var basis = new TensorBasis(assetDim, variantDim);
        Assert.Equal(expected, basis.ToLinear(assetIdx, variantIdx));
    }

    [Fact]
    public void MarketTensorBasis_ConstructorSetsProperties()
    {
        var basis = new MarketTensorBasis(100, 50, 20);

        Assert.Equal(100, basis.InstrumentDimension);
        Assert.Equal(50, basis.PriceLevelDimension);
        Assert.Equal(20, basis.OrderSlotDimension);
        Assert.Equal(100_000, basis.Rank);
    }

    [Fact]
    public void MarketTensorBasis_ToLinearMapsCorrectly()
    {
        var basis = new MarketTensorBasis(10, 5, 3);

        Assert.Equal(0, basis.ToLinear(0, 0, 0));    // First element
        Assert.Equal(1, basis.ToLinear(0, 0, 1));    // Second slot
        Assert.Equal(3, basis.ToLinear(0, 1, 0));    // First slot of second price level
        Assert.Equal(15, basis.ToLinear(1, 0, 0));   // First slot of second instrument
        Assert.Equal(149, basis.ToLinear(9, 4, 2));  // Last element
    }

    [Fact]
    public void MarketTensorBasis_GetPriceLevelRangeReturnsCorrectRange()
    {
        var basis = new MarketTensorBasis(10, 5, 3);

        var (start, length) = basis.GetPriceLevelRange(0, 0);
        Assert.Equal(0, start);
        Assert.Equal(3, length);

        var (start2, length2) = basis.GetPriceLevelRange(1, 2);
        Assert.Equal(21, start2);  // (1 * 5 + 2) * 3 = 21
        Assert.Equal(3, length2);
    }

    [Fact]
    public void MarketTensorBasis_TypicalL3Configuration()
    {
        // Typical L3 configuration from proposal
        var basis = new MarketTensorBasis(500, 200, 100);

        Assert.Equal(10_000_000, basis.Rank);

        // Verify first order slot of first price level of first instrument
        Assert.Equal(0, basis.ToLinear(0, 0, 0));

        // Verify last order slot
        Assert.Equal(9_999_999, basis.ToLinear(499, 199, 99));
    }

    [Theory]
    [InlineData(2, 3, 4, 0, 0, 0, 0)]
    [InlineData(2, 3, 4, 0, 0, 3, 3)]
    [InlineData(2, 3, 4, 0, 1, 0, 4)]
    [InlineData(2, 3, 4, 1, 0, 0, 12)]
    [InlineData(2, 3, 4, 1, 2, 3, 23)]
    public void MarketTensorBasis_ToLinearFormula(
        int instDim, int priceDim, int slotDim,
        int instIdx, int priceIdx, int slotIdx,
        int expected)
    {
        var basis = new MarketTensorBasis(instDim, priceDim, slotDim);
        Assert.Equal(expected, basis.ToLinear(instIdx, priceIdx, slotIdx));
    }
}
