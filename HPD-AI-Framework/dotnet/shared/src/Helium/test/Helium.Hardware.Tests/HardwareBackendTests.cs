using Helium.Hardware;

namespace Helium.Hardware.Tests;

public class HardwareBackendTests
{
    [Fact]
    public void LocalHardwareBackend_DoubleMatMul_ReturnsNewTensor()
    {
        var backend = new LocalHardwareBackend<double>();
        using var a = backend.CreateMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        using var b = backend.CreateMatrix(2, 2, [5.0, 6.0, 7.0, 8.0]);

        using var result = backend.MatMul(a, b);

        var output = new double[4];
        result.CopyToHost(output);
        Assert.Equal([19.0, 22.0, 43.0, 50.0], output);

        var original = new double[4];
        a.CopyToHost(original);
        Assert.Equal([1.0, 2.0, 3.0, 4.0], original);
    }

    [Fact]
    public void LocalHardwareBackend_FloatElementwiseOps_AreImmutable()
    {
        var backend = new LocalHardwareBackend<float>();
        using var a = backend.CreateMatrix(1, 3, [1.0f, -2.0f, 3.0f]);
        using var b = backend.CreateMatrix(1, 3, [4.0f, 5.0f, -6.0f]);

        using var sum = backend.Add(a, b);
        using var difference = backend.Subtract(a, b);
        using var product = backend.Multiply(a, b);
        using var negated = backend.Negate(a);
        using var scaled = backend.Scale(a, 2.0f);

        var output = new float[3];
        sum.CopyToHost(output);
        Assert.Equal([5.0f, 3.0f, -3.0f], output);

        difference.CopyToHost(output);
        Assert.Equal([-3.0f, -7.0f, 9.0f], output);

        product.CopyToHost(output);
        Assert.Equal([4.0f, -10.0f, -18.0f], output);

        negated.CopyToHost(output);
        Assert.Equal([-1.0f, 2.0f, -3.0f], output);

        scaled.CopyToHost(output);
        Assert.Equal([2.0f, -4.0f, 6.0f], output);

        a.CopyToHost(output);
        Assert.Equal([1.0f, -2.0f, 3.0f], output);
    }

    [Fact]
    public void LocalHardwareBackend_DoubleReductions_UseTensorPrimitives()
    {
        var backend = new LocalHardwareBackend<double>();
        using var a = backend.CreateMatrix(1, 3, [3.0, 4.0, 12.0]);
        using var b = backend.CreateMatrix(1, 3, [2.0, 3.0, 4.0]);

        Assert.Equal(19.0, backend.Sum(a));
        Assert.Equal(19.0 / 3.0, backend.Mean(a));
        Assert.Equal(66.0, backend.Dot(a, b));
        Assert.Equal(13.0, backend.Norm(a));
    }

    [Fact]
    public void HardwareTensor_UpdateFromSpan_RequiresExactLength()
    {
        var backend = new LocalHardwareBackend<double>();
        using var tensor = backend.CreateMatrix(1, 2, [1.0, 2.0]);

        tensor.UpdateFromSpan([3.0, 4.0]);
        var output = new double[2];
        tensor.CopyToHost(output);
        Assert.Equal([3.0, 4.0], output);

        Assert.Throws<ArgumentException>(() => tensor.UpdateFromSpan([1.0]));
    }
}
