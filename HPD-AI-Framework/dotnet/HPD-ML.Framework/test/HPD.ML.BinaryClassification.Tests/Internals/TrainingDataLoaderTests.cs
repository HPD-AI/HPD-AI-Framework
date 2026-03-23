namespace HPD.ML.BinaryClassification.Tests;

using Helium.Primitives;
using Double = Helium.Primitives.Double;

public class TrainingDataLoaderTests
{
    [Fact]
    public void Load_FloatArrayFeatures()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f, 2f], [3f, 4f] }),
            ("Label", new bool[] { true, false }));

        var (features, labels, featureCount) = TrainingDataLoader.Load(data, "Features", "Label");

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
            ("Label", new bool[] { true, false }));

        var (features, labels, featureCount) = TrainingDataLoader.Load(data, "Features", "Label");

        Assert.Equal(1, featureCount);
        Assert.Equal(5.0, (double)features[0][0], 0.001);
    }

    [Fact]
    public void Load_BoolLabels()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f] }),
            ("Label", new bool[] { true, false, true }));

        var (_, labels, _) = TrainingDataLoader.Load(data, "Features", "Label");

        Assert.Equal([true, false, true], labels);
    }

    [Fact]
    public void Load_IntLabels_ConvertedToBool()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f] }),
            ("Label", new int[] { 1, 0, 1 }));

        var (_, labels, _) = TrainingDataLoader.Load(data, "Features", "Label");

        Assert.Equal([true, false, true], labels);
    }

    [Fact]
    public void Load_EmptyData_ReturnsEmpty()
    {
        var data = TestHelper.Data(
            ("Features", new float[0][]),
            ("Label", new bool[0]));

        var (features, labels, _) = TrainingDataLoader.Load(data, "Features", "Label");

        Assert.Empty(features);
        Assert.Empty(labels);
    }
}
