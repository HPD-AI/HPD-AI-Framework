using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class TensorHelperTests
{
    [Fact]
    public void AsScalarBatch_Returns1DTensor()
    {
        var array = new float[] { 0f, 1f, 2f, 3f, 4f };
        var batch = TensorHelpers.AsScalarBatch(array, 1, 3);

        Assert.Equal(3, batch.FlattenedLength);
    }

    [Fact]
    public void AsScalarBatch_ClampsToArrayLength()
    {
        var array = new float[] { 0f, 1f, 2f, 3f, 4f };
        var batch = TensorHelpers.AsScalarBatch(array, 3, 10);

        Assert.Equal(2, batch.FlattenedLength);
    }

    [Fact]
    public void AsVectorBatch_Returns2DTensor()
    {
        // 3 rows of 4-element vectors = 12 elements
        var array = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        var batch = TensorHelpers.AsVectorBatch(array, 0, 2, 4);

        Assert.Equal(2, batch.Lengths[0]); // 2 rows
        Assert.Equal(4, batch.Lengths[1]); // 4 elements per row
    }

    [Fact]
    public void AsVectorBatch_PartialRange()
    {
        var array = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        var batch = TensorHelpers.AsVectorBatch(array, 1, 5, 4);

        // Only 2 rows available starting at row 1 (of 3 total)
        Assert.Equal(2, batch.Lengths[0]);
        Assert.Equal(4, batch.Lengths[1]);
    }
}
