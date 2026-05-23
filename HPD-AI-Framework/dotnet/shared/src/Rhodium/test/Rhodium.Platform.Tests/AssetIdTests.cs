using Rhodium.Primitives;

namespace Rhodium.Platform.Tests;

/// <summary>
/// Tests for AssetId topology-bound handle.
/// </summary>
public class AssetIdTests
{
    [Fact]
    public void AssetId_ConstructsWithVirtualIndex()
    {
        var id = new AssetId(42);
        Assert.Equal(42, id.VirtualIndex);
    }

    [Fact]
    public void AssetId_ImplicitConversionToInt()
    {
        var id = new AssetId(123);
        int index = id;
        Assert.Equal(123, index);
    }

    [Fact]
    public void AssetId_WithVariant_AddsOffset()
    {
        var id = new AssetId(100);
        var variant = id.WithVariant(5);

        Assert.Equal(100, id.VirtualIndex);
        Assert.Equal(105, variant.VirtualIndex);
    }

    [Fact]
    public void AssetId_WithVariant_NegativeOffset()
    {
        var id = new AssetId(50);
        var variant = id.WithVariant(-10);

        Assert.Equal(40, variant.VirtualIndex);
    }

    [Fact]
    public void AssetId_WithVariant_ZeroOffset()
    {
        var id = new AssetId(75);
        var variant = id.WithVariant(0);

        Assert.Equal(75, variant.VirtualIndex);
    }

    [Fact]
    public void AssetId_ToString_ReturnsFormattedString()
    {
        var id = new AssetId(42);
        Assert.Equal("AssetId(42)", id.ToString());
    }

    [Fact]
    public void AssetId_EqualityWorks()
    {
        var id1 = new AssetId(100);
        var id2 = new AssetId(100);
        var id3 = new AssetId(200);

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void AssetId_RecordStruct_Immutable()
    {
        var id = new AssetId(50);
        var variant = id.WithVariant(10);

        // Original should remain unchanged
        Assert.Equal(50, id.VirtualIndex);
        Assert.Equal(60, variant.VirtualIndex);
    }

    [Fact]
    public void AssetId_ChainVariants()
    {
        var base1 = new AssetId(100);
        var variant1 = base1.WithVariant(10);
        var variant2 = variant1.WithVariant(5);

        Assert.Equal(100, base1.VirtualIndex);
        Assert.Equal(110, variant1.VirtualIndex);
        Assert.Equal(115, variant2.VirtualIndex);
    }

    [Fact]
    public void AssetId_ZeroIndex()
    {
        var id = new AssetId(0);
        Assert.Equal(0, id.VirtualIndex);
    }

    [Fact]
    public void AssetId_LargeIndex()
    {
        var id = new AssetId(int.MaxValue);
        Assert.Equal(int.MaxValue, id.VirtualIndex);
    }

    [Fact]
    public void AssetId_ImplicitConversion_InExpression()
    {
        var id = new AssetId(10);
        int result = id * 2 + 5;
        Assert.Equal(25, result);
    }
}
