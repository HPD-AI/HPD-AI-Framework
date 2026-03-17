namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MissingValueDropTests
{
    [Fact]
    public void Drop_RemovesMissingRows()
    {
        var data = TestHelper.Data(
            ("V", new float[] { 1, float.NaN, 3, float.NaN, 5 }));
        var transform = new MissingValueDropTransform("V");
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(3, values.Count);
        Assert.Equal([1f, 3f, 5f], values);
    }

    [Fact]
    public void Drop_MultipleColumns()
    {
        var data = TestHelper.Data(
            ("A", new float[] { 1, 2, float.NaN }),
            ("B", new float[] { float.NaN, 2, 3 }));
        var transform = new MissingValueDropTransform("A", "B");
        var result = transform.Apply(data);
        var count = TestHelper.CountRows(result);
        Assert.Equal(1, count); // Only row index 1 has both valid
    }

    [Fact]
    public void Drop_NoMissing_AllKept()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 2, 3 }));
        var transform = new MissingValueDropTransform("V");
        var result = transform.Apply(data);
        Assert.Equal(3, TestHelper.CountRows(result));
    }

    [Fact]
    public void Drop_PreservesRowCount_False()
    {
        var transform = new MissingValueDropTransform("V");
        Assert.False(transform.Properties.PreservesRowCount);
    }
}
