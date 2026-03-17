using Rhodium.Quant;

namespace Rhodium.Quant.Tests;

/// <summary>
/// Tests for SymmetricTensor packed matrix storage.
/// </summary>
public class SymmetricTensorTests
{
    [Fact]
    public void Constructor_SetsCorrectDimension()
    {
        using var tensor = new SymmetricTensor(5);

        Assert.Equal(5, tensor.Dimension);
    }

    [Fact]
    public void Constructor_CalculatesCorrectPackedLength()
    {
        using var tensor = new SymmetricTensor(5);

        // N*(N+1)/2 = 5*6/2 = 15
        Assert.Equal(15, tensor.PackedLength);
    }

    [Fact]
    public void Constructor_ThrowsOnZeroDimension()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SymmetricTensor(0));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeDimension()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SymmetricTensor(-1));
    }

    [Fact]
    public void Constructor_InitializesToZero()
    {
        using var tensor = new SymmetricTensor(3);

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Assert.Equal(0.0, tensor[i, j]);
            }
        }
    }

    [Fact]
    public void Indexer_SetAndGet_ReturnsCorrectValue()
    {
        using var tensor = new SymmetricTensor(3);

        tensor[1, 2] = 42.5;

        Assert.Equal(42.5, tensor[1, 2]);
    }

    [Fact]
    public void Indexer_SymmetryMaintained()
    {
        using var tensor = new SymmetricTensor(4);

        tensor[1, 3] = 100.0;

        // T[i,j] == T[j,i]
        Assert.Equal(100.0, tensor[1, 3]);
        Assert.Equal(100.0, tensor[3, 1]);
    }

    [Fact]
    public void Indexer_DiagonalElements_Work()
    {
        using var tensor = new SymmetricTensor(3);

        tensor[0, 0] = 1.0;
        tensor[1, 1] = 2.0;
        tensor[2, 2] = 3.0;

        Assert.Equal(1.0, tensor[0, 0]);
        Assert.Equal(2.0, tensor[1, 1]);
        Assert.Equal(3.0, tensor[2, 2]);
    }

    [Fact]
    public void Indexer_ThrowsOnInvalidRow()
    {
        using var tensor = new SymmetricTensor(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => tensor[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => tensor[-1, 0]);
    }

    [Fact]
    public void Indexer_ThrowsOnInvalidColumn()
    {
        using var tensor = new SymmetricTensor(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => tensor[0, 3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => tensor[0, -1]);
    }

    [Fact]
    public void PackedData_ReturnsCorrectLength()
    {
        using var tensor = new SymmetricTensor(4);

        var packed = tensor.PackedData;

        Assert.Equal(10, packed.Length); // 4*5/2 = 10
    }

    [Fact]
    public void PackedData_CanModifyDirectly()
    {
        using var tensor = new SymmetricTensor(3);

        var packed = tensor.PackedData;
        packed[0] = 99.0; // This is T[0,0]

        Assert.Equal(99.0, tensor[0, 0]);
    }

    [Fact]
    public void Clear_ResetsAllElementsToZero()
    {
        using var tensor = new SymmetricTensor(3);

        tensor[0, 0] = 1.0;
        tensor[1, 2] = 2.0;
        tensor[2, 2] = 3.0;

        tensor.Clear();

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Assert.Equal(0.0, tensor[i, j]);
            }
        }
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var tensor = new SymmetricTensor(3);

        tensor.Dispose();
        tensor.Dispose(); // Should not throw
    }

    [Fact]
    public void Indexer_AfterDispose_Throws()
    {
        var tensor = new SymmetricTensor(3);
        tensor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tensor[0, 0]);
    }

    [Fact]
    public void PackedData_AfterDispose_Throws()
    {
        var tensor = new SymmetricTensor(3);
        tensor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { var _ = tensor.PackedData; });
    }

    [Fact]
    public void Clear_AfterDispose_Throws()
    {
        var tensor = new SymmetricTensor(3);
        tensor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tensor.Clear());
    }

    [Fact]
    public void LargeTensor_AllocatesCorrectly()
    {
        using var tensor = new SymmetricTensor(100);

        Assert.Equal(100, tensor.Dimension);
        Assert.Equal(5050, tensor.PackedLength); // 100*101/2
    }

    [Fact]
    public void FillMatrix_VerifySymmetry()
    {
        using var tensor = new SymmetricTensor(5);

        // Fill upper triangle
        for (int i = 0; i < 5; i++)
        {
            for (int j = i; j < 5; j++)
            {
                tensor[i, j] = i * 10 + j;
            }
        }

        // Verify symmetry
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                double expected = Math.Min(i, j) * 10 + Math.Max(i, j);
                Assert.Equal(expected, tensor[i, j]);
            }
        }
    }

    [Fact]
    public void NegativeValues_StoredCorrectly()
    {
        using var tensor = new SymmetricTensor(3);

        tensor[0, 1] = -42.5;
        tensor[1, 2] = -100.0;

        Assert.Equal(-42.5, tensor[0, 1]);
        Assert.Equal(-42.5, tensor[1, 0]);
        Assert.Equal(-100.0, tensor[1, 2]);
        Assert.Equal(-100.0, tensor[2, 1]);
    }

    [Fact]
    public void SmallValues_PreservedPrecision()
    {
        using var tensor = new SymmetricTensor(2);

        tensor[0, 1] = 1e-15;

        Assert.Equal(1e-15, tensor[0, 1]);
        Assert.Equal(1e-15, tensor[1, 0]);
    }
}
