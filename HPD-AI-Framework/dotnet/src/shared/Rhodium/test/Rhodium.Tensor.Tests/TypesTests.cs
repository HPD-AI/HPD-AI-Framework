using Rhodium.Tensor;
using System.Runtime.InteropServices;

namespace Rhodium.Tensor.Tests;

public class TypesTests
{
    [Fact]
    public void PriceF64_SizeIsEightBytes()
    {
        Assert.Equal(8, Marshal.SizeOf<PriceF64>());
    }

    [Fact]
    public void SizeF64_SizeIsEightBytes()
    {
        Assert.Equal(8, Marshal.SizeOf<SizeF64>());
    }

    [Fact]
    public void FactorF64_SizeIsEightBytes()
    {
        Assert.Equal(8, Marshal.SizeOf<FactorF64>());
    }

    [Fact]
    public void PriceF64_StoresValueCorrectly()
    {
        var price = new PriceF64(123.45);
        Assert.Equal(123.45, price.Value);
    }

    [Fact]
    public void SizeF64_StoresValueCorrectly()
    {
        var size = new SizeF64(1000.0);
        Assert.Equal(1000.0, size.Value);
    }

    [Fact]
    public void FactorF64_StoresValueCorrectly()
    {
        var factor = new FactorF64(0.5);
        Assert.Equal(0.5, factor.Value);
    }

    [Fact]
    public void PriceF64_EqualityWorks()
    {
        var p1 = new PriceF64(100.0);
        var p2 = new PriceF64(100.0);
        var p3 = new PriceF64(200.0);

        Assert.Equal(p1, p2);
        Assert.NotEqual(p1, p3);
    }

    [Fact]
    public void SizeF64_EqualityWorks()
    {
        var s1 = new SizeF64(500.0);
        var s2 = new SizeF64(500.0);
        var s3 = new SizeF64(1000.0);

        Assert.Equal(s1, s2);
        Assert.NotEqual(s1, s3);
    }

    [Fact]
    public void FactorF64_EqualityWorks()
    {
        var f1 = new FactorF64(1.0);
        var f2 = new FactorF64(1.0);
        var f3 = new FactorF64(0.5);

        Assert.Equal(f1, f2);
        Assert.NotEqual(f1, f3);
    }

    [Fact]
    public unsafe void PriceF64_MemoryLayoutIsSequential()
    {
        var price = new PriceF64(123.45);
        var ptr = (double*)&price;
        Assert.Equal(123.45, *ptr);
    }

    [Fact]
    public unsafe void SizeF64_MemoryLayoutIsSequential()
    {
        var size = new SizeF64(1000.0);
        var ptr = (double*)&size;
        Assert.Equal(1000.0, *ptr);
    }

    [Fact]
    public unsafe void FactorF64_MemoryLayoutIsSequential()
    {
        var factor = new FactorF64(0.5);
        var ptr = (double*)&factor;
        Assert.Equal(0.5, *ptr);
    }

    [Fact]
    public void VectorField_StoresNameCorrectly()
    {
        var field = new VectorField<PriceF64>("Close");
        Assert.Equal("Close", field.Name);
    }

    [Fact]
    public void VectorField_EqualityWorks()
    {
        var f1 = new VectorField<PriceF64>("Close");
        var f2 = new VectorField<PriceF64>("Close");
        var f3 = new VectorField<PriceF64>("Open");

        Assert.Equal(f1, f2);
        Assert.NotEqual(f1, f3);
    }

    [Fact]
    public void VectorField_DifferentTypesNotEqual()
    {
        var priceField = new VectorField<PriceF64>("Value");
        var sizeField = new VectorField<SizeF64>("Value");

        // Different types, even with same name
        Assert.False(priceField.Equals(sizeField));
    }
}
