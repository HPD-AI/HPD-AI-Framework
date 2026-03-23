namespace HPD.ML.Regression.Tests;

using Helium.Primitives;
using Double = Helium.Primitives.Double;

public class RegressionDataLoaderTests
{
    [Fact]
    public void Load_FloatArrayFeatures()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f, 2f], [3f, 4f] }),
            ("Label", new float[] { 10f, 20f }));

        var (features, labels, featureCount) = RegressionDataLoader.Load(data, "Features", "Label");

        Assert.Equal(2, featureCount);
        Assert.Equal(2, features.Count);
        Assert.Equal(1.0, (double)features[0][0], 0.001);
        Assert.Equal(2.0, (double)features[0][1], 0.001);
    }

    [Fact]
    public void Load_ScalarFeatures()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [5f], [10f] }),
            ("Label", new float[] { 1f, 2f }));

        var (features, labels, featureCount) = RegressionDataLoader.Load(data, "Features", "Label");

        Assert.Equal(1, featureCount);
        Assert.Equal(5.0, (double)features[0][0], 0.001);
    }

    [Fact]
    public void Load_FloatLabels()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f] }),
            ("Label", new float[] { 1.5f, 2.5f, 3.5f }));

        var (_, labels, _) = RegressionDataLoader.Load(data, "Features", "Label");

        Assert.Equal(1.5, labels[0], 0.01);
        Assert.Equal(2.5, labels[1], 0.01);
        Assert.Equal(3.5, labels[2], 0.01);
    }

    [Fact]
    public void Load_IntLabels_ConvertedToDouble()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f] }),
            ("Label", new int[] { 1, 0, 3 }));

        var (_, labels, _) = RegressionDataLoader.Load(data, "Features", "Label");

        Assert.Equal([1.0, 0.0, 3.0], labels);
    }

    [Fact]
    public void Load_EmptyData_ReturnsEmpty()
    {
        var data = TestHelper.Data(
            ("Features", new float[0][]),
            ("Label", new float[0]));

        var (features, labels, _) = RegressionDataLoader.Load(data, "Features", "Label");

        Assert.Empty(features);
        Assert.Empty(labels);
    }
}
