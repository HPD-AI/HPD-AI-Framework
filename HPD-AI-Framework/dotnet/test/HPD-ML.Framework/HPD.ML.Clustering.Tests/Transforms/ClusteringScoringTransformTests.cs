namespace HPD.ML.Clustering.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class ClusteringScoringTransformTests
{
    private static ClusteringScoringTransform CreateTransform(int k = 3)
    {
        // K centroids in 2D, well separated
        var centroids = new float[k * 2];
        for (int i = 0; i < k; i++)
        {
            centroids[i * 2] = i * 20f;
            centroids[i * 2 + 1] = 0f;
        }
        return new ClusteringScoringTransform(
            new ClusteringModelParameters(k, 2, centroids));
    }

    [Fact]
    public void GetOutputSchema_AddsPredictedLabelAndScore()
    {
        var transform = CreateTransform(3);
        var inputSchema = new Schema([new Column("Features", FieldType.Vector<float>(2))]);
        var outputSchema = transform.GetOutputSchema(inputSchema);

        Assert.NotNull(outputSchema.FindByName("PredictedLabel"));
        Assert.NotNull(outputSchema.FindByName("Score"));
    }

    [Fact]
    public void Apply_AssignsCorrectCluster()
    {
        // centroids: [0,0] and [10,10]
        var p = new ClusteringModelParameters(2, 2, [0f, 0f, 10f, 10f]);
        var transform = new ClusteringScoringTransform(p);
        var data = TestHelper.Data(("Features", new float[][] { [1f, 1f] }));

        var output = transform.Apply(data);
        var labels = TestHelper.CollectUInt(output, "PredictedLabel");
        Assert.Single(labels);
        Assert.Equal(1u, labels[0]); // nearest to [0,0] → cluster 0 → label 1
    }

    [Fact]
    public void Apply_ScoreContainsAllDistances()
    {
        var transform = CreateTransform(3);
        var data = TestHelper.Data(("Features", new float[][] { [5f, 0f] }));

        var output = transform.Apply(data);
        var scores = TestHelper.CollectFloatArray(output, "Score");
        Assert.Single(scores);
        Assert.Equal(3, scores[0].Length);
        Assert.All(scores[0], s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void Apply_PreservesInputColumns()
    {
        var transform = CreateTransform(2);
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f, 2f] }),
            ("Name", new string[] { "test" }));

        var output = transform.Apply(data);
        Assert.NotNull(output.Schema.FindByName("Features"));
        Assert.NotNull(output.Schema.FindByName("Name"));
        Assert.NotNull(output.Schema.FindByName("PredictedLabel"));
        Assert.NotNull(output.Schema.FindByName("Score"));
    }

    [Fact]
    public void Apply_PreservesRowCount()
    {
        var transform = CreateTransform(2);
        var features = new float[20][];
        for (int i = 0; i < 20; i++) features[i] = [i * 1f, 0f];
        var data = TestHelper.Data(("Features", features));

        var output = transform.Apply(data);
        Assert.Equal(20, TestHelper.CountRows(output));
    }

    [Fact]
    public void Apply_OneIndexedLabels()
    {
        var transform = CreateTransform(2);
        var features = new float[10][];
        for (int i = 0; i < 10; i++) features[i] = [i * 5f, 0f];
        var data = TestHelper.Data(("Features", features));

        var output = transform.Apply(data);
        var labels = TestHelper.CollectUInt(output, "PredictedLabel");
        Assert.All(labels, l => Assert.True(l == 1u || l == 2u));
    }
}
