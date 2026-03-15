using Rhodium.Tensor;
using System.Numerics.Tensors;

namespace Rhodium.Tensor.Tests;

public class TensorMarshalTests
{
    [Fact]
    public void TensorMarshal_AsDoublesConvertsCorrectly()
    {
        Span<PriceF64> prices = stackalloc PriceF64[5];
        prices[0] = new PriceF64(100.0);
        prices[1] = new PriceF64(200.0);
        prices[2] = new PriceF64(300.0);
        prices[3] = new PriceF64(400.0);
        prices[4] = new PriceF64(500.0);

        var doubles = TensorMarshal.AsDoubles(prices);

        Assert.Equal(5, doubles.Length);
        Assert.Equal(100.0, doubles[0]);
        Assert.Equal(200.0, doubles[1]);
        Assert.Equal(300.0, doubles[2]);
        Assert.Equal(400.0, doubles[3]);
        Assert.Equal(500.0, doubles[4]);
    }

    [Fact]
    public void TensorMarshal_AsReadOnlyDoublesConvertsCorrectly()
    {
        ReadOnlySpan<SizeF64> sizes = stackalloc SizeF64[3]
        {
            new SizeF64(1000.0),
            new SizeF64(2000.0),
            new SizeF64(3000.0)
        };

        var doubles = TensorMarshal.AsReadOnlyDoubles(sizes);

        Assert.Equal(3, doubles.Length);
        Assert.Equal(1000.0, doubles[0]);
        Assert.Equal(2000.0, doubles[1]);
        Assert.Equal(3000.0, doubles[2]);
    }

    [Fact]
    public void TensorMarshal_ModificationThroughDoublesReflectsInOriginal()
    {
        Span<PriceF64> prices = stackalloc PriceF64[3];
        prices[0] = new PriceF64(100.0);
        prices[1] = new PriceF64(200.0);
        prices[2] = new PriceF64(300.0);

        var doubles = TensorMarshal.AsDoubles(prices);
        doubles[1] = 999.0;

        // Should reflect in original span
        Assert.Equal(999.0, prices[1].Value);
    }

    [Fact]
    public void TensorMarshal_WorksWithTensorPrimitivesAdd()
    {
        Span<PriceF64> a = stackalloc PriceF64[4];
        Span<PriceF64> b = stackalloc PriceF64[4];
        Span<PriceF64> result = stackalloc PriceF64[4];

        a[0] = new PriceF64(10.0);
        a[1] = new PriceF64(20.0);
        a[2] = new PriceF64(30.0);
        a[3] = new PriceF64(40.0);

        b[0] = new PriceF64(1.0);
        b[1] = new PriceF64(2.0);
        b[2] = new PriceF64(3.0);
        b[3] = new PriceF64(4.0);

        var aDoubles = TensorMarshal.AsReadOnlyDoubles(a);
        var bDoubles = TensorMarshal.AsReadOnlyDoubles(b);
        var resultDoubles = TensorMarshal.AsDoubles(result);

        TensorPrimitives.Add(aDoubles, bDoubles, resultDoubles);

        Assert.Equal(11.0, result[0].Value);
        Assert.Equal(22.0, result[1].Value);
        Assert.Equal(33.0, result[2].Value);
        Assert.Equal(44.0, result[3].Value);
    }

    [Fact]
    public void TensorMarshal_WorksWithTensorPrimitivesMultiply()
    {
        Span<PriceF64> prices = stackalloc PriceF64[4];
        Span<FactorF64> factors = stackalloc FactorF64[4];
        Span<PriceF64> adjusted = stackalloc PriceF64[4];

        prices[0] = new PriceF64(100.0);
        prices[1] = new PriceF64(200.0);
        prices[2] = new PriceF64(300.0);
        prices[3] = new PriceF64(400.0);

        factors[0] = new FactorF64(0.5);
        factors[1] = new FactorF64(0.5);
        factors[2] = new FactorF64(0.5);
        factors[3] = new FactorF64(0.5);

        var pricesDoubles = TensorMarshal.AsReadOnlyDoubles(prices);
        var factorsDoubles = TensorMarshal.AsReadOnlyDoubles(factors);
        var adjustedDoubles = TensorMarshal.AsDoubles(adjusted);

        TensorPrimitives.Multiply(pricesDoubles, factorsDoubles, adjustedDoubles);

        Assert.Equal(50.0, adjusted[0].Value);
        Assert.Equal(100.0, adjusted[1].Value);
        Assert.Equal(150.0, adjusted[2].Value);
        Assert.Equal(200.0, adjusted[3].Value);
    }

    [Fact]
    public void TensorMarshal_WorksWithTensorPrimitivesDivide()
    {
        Span<SizeF64> volumes = stackalloc SizeF64[4];
        Span<FactorF64> divisors = stackalloc FactorF64[4];
        Span<SizeF64> results = stackalloc SizeF64[4];

        volumes[0] = new SizeF64(1000.0);
        volumes[1] = new SizeF64(2000.0);
        volumes[2] = new SizeF64(3000.0);
        volumes[3] = new SizeF64(4000.0);

        divisors[0] = new FactorF64(2.0);
        divisors[1] = new FactorF64(2.0);
        divisors[2] = new FactorF64(2.0);
        divisors[3] = new FactorF64(2.0);

        TensorPrimitives.Divide(
            TensorMarshal.AsReadOnlyDoubles(volumes),
            TensorMarshal.AsReadOnlyDoubles(divisors),
            TensorMarshal.AsDoubles(results));

        Assert.Equal(500.0, results[0].Value);
        Assert.Equal(1000.0, results[1].Value);
        Assert.Equal(1500.0, results[2].Value);
        Assert.Equal(2000.0, results[3].Value);
    }

    [Fact]
    public void TensorMarshal_WorksWithTensorPrimitivesMax()
    {
        Span<PriceF64> prices = stackalloc PriceF64[5];
        prices[0] = new PriceF64(100.0);
        prices[1] = new PriceF64(500.0);
        prices[2] = new PriceF64(200.0);
        prices[3] = new PriceF64(300.0);
        prices[4] = new PriceF64(150.0);

        var max = TensorPrimitives.Max(TensorMarshal.AsReadOnlyDoubles(prices));

        Assert.Equal(500.0, max);
    }

    [Fact]
    public void TensorMarshal_WorksWithTensorPrimitivesSum()
    {
        Span<SizeF64> volumes = stackalloc SizeF64[4];
        volumes[0] = new SizeF64(100.0);
        volumes[1] = new SizeF64(200.0);
        volumes[2] = new SizeF64(300.0);
        volumes[3] = new SizeF64(400.0);

        var sum = TensorPrimitives.Sum(TensorMarshal.AsReadOnlyDoubles(volumes));

        Assert.Equal(1000.0, sum);
    }

    [Fact]
    public void TensorMarshal_LargeSpanConversion()
    {
        // Test with larger span to ensure no overhead
        var prices = new PriceF64[10000];
        for (int i = 0; i < prices.Length; i++)
            prices[i] = new PriceF64(i * 1.5);

        var doubles = TensorMarshal.AsReadOnlyDoubles(prices.AsSpan());

        Assert.Equal(10000, doubles.Length);
        Assert.Equal(0.0, doubles[0]);
        Assert.Equal(1.5, doubles[1]);
        Assert.Equal(14998.5, doubles[9999]);
    }

    [Fact]
    public void TensorMarshal_ZeroLengthSpan()
    {
        Span<PriceF64> empty = Span<PriceF64>.Empty;
        var doubles = TensorMarshal.AsDoubles(empty);

        Assert.Equal(0, doubles.Length);
    }

    [Fact]
    public void TensorMarshal_WorksWithFactorF64()
    {
        Span<FactorF64> factors = stackalloc FactorF64[3];
        factors[0] = new FactorF64(1.0);
        factors[1] = new FactorF64(0.5);
        factors[2] = new FactorF64(2.0);

        var doubles = TensorMarshal.AsReadOnlyDoubles(factors);

        Assert.Equal(1.0, doubles[0]);
        Assert.Equal(0.5, doubles[1]);
        Assert.Equal(2.0, doubles[2]);
    }
}
