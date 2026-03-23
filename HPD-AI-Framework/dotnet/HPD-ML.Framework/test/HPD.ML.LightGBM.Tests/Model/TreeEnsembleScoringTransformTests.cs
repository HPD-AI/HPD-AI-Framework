namespace HPD.ML.LightGBM.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class TreeEnsembleScoringTransformTests
{
    private static ISchema FeatureSchema()
        => new SchemaBuilder()
            .AddVectorColumn<float>("Features", 2)
            .Build();

    [Fact]
    public void GetOutputSchema_Regression_HasScoreOnly()
    {
        var ensemble = new TreeEnsemble([], 0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.Regression);

        var schema = transform.GetOutputSchema(FeatureSchema());
        Assert.NotNull(schema.FindByName("Score"));
        Assert.Null(schema.FindByName("Probability"));
        Assert.Null(schema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void GetOutputSchema_Binary_HasScoreProbabilityLabel()
    {
        var ensemble = new TreeEnsemble([], 0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.BinaryClassification);

        var schema = transform.GetOutputSchema(FeatureSchema());
        Assert.NotNull(schema.FindByName("Score"));
        Assert.NotNull(schema.FindByName("Probability"));
        Assert.NotNull(schema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void GetOutputSchema_Multiclass_HasScoreVectorAndLabel()
    {
        var ensemble = new TreeEnsemble([], 0, numberOfClasses: 3);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.Multiclass, numberOfClasses: 3);

        var schema = transform.GetOutputSchema(FeatureSchema());
        var scoreCol = schema.FindByName("Score");
        Assert.NotNull(scoreCol);
        Assert.True(scoreCol!.Type.IsVector);
        Assert.NotNull(schema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void Apply_Regression_ScoresCorrectly()
    {
        var tree = TestHelper.SingleLeafTree(2.0);
        var ensemble = new TreeEnsemble([tree], bias: 1.0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.Regression);

        var data = TestHelper.Data(
            [new float[] { 1f, 2f }, new float[] { 3f, 4f }],
            [0f, 1f]);

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");

        Assert.Equal(2, scores.Count);
        Assert.Equal(3.0f, scores[0], 1e-5f);  // bias(1) + leaf(2)
        Assert.Equal(3.0f, scores[1], 1e-5f);
    }

    [Fact]
    public void Apply_Binary_SigmoidAndThreshold()
    {
        // leaf=2.0 → sigmoid(2.0) ≈ 0.8808 → predicted=true
        var tree = TestHelper.SingleLeafTree(2.0);
        var ensemble = new TreeEnsemble([tree], bias: 0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.BinaryClassification);

        var data = TestHelper.Data([new float[] { 1f, 2f }], [1f]);
        var result = transform.Apply(data);

        var probs = TestHelper.CollectFloat(result, "Probability");
        var preds = TestHelper.CollectBool(result, "PredictedLabel");

        Assert.Equal(0.8808f, probs[0], 1e-3f);
        Assert.True(preds[0]);
    }

    [Fact]
    public void Apply_Binary_NegativeScore_PredictsFalse()
    {
        // leaf=-2.0 → sigmoid(-2.0) ≈ 0.1192 → predicted=false
        var tree = TestHelper.SingleLeafTree(-2.0);
        var ensemble = new TreeEnsemble([tree], bias: 0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.BinaryClassification);

        var data = TestHelper.Data([new float[] { 1f, 2f }], [0f]);
        var result = transform.Apply(data);

        var probs = TestHelper.CollectFloat(result, "Probability");
        var preds = TestHelper.CollectBool(result, "PredictedLabel");

        Assert.Equal(0.1192f, probs[0], 1e-3f);
        Assert.False(preds[0]);
    }

    [Fact]
    public void Apply_Multiclass_SoftmaxAndArgmax()
    {
        // 3 classes, interleaved trees: class0=1.0, class1=5.0, class2=2.0
        var trees = new[]
        {
            TestHelper.SingleLeafTree(1.0),  // class 0
            TestHelper.SingleLeafTree(5.0),  // class 1
            TestHelper.SingleLeafTree(2.0),  // class 2
        };
        var ensemble = new TreeEnsemble(trees, bias: 0, numberOfClasses: 3);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.Multiclass, numberOfClasses: 3);

        var data = TestHelper.Data([new float[] { 1f, 2f }], [1f]);
        var result = transform.Apply(data);

        var preds = TestHelper.CollectUint(result, "PredictedLabel");
        Assert.Equal(1u, preds[0]);  // class 1 has highest score (5.0)
    }

    [Fact]
    public void Apply_PreservesInputColumns()
    {
        var tree = TestHelper.SingleLeafTree(1.0);
        var ensemble = new TreeEnsemble([tree], bias: 0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.Regression);

        var data = TestHelper.Data([new float[] { 1f, 2f }], [42f]);
        var result = transform.Apply(data);

        // Input columns should be preserved
        var labels = TestHelper.CollectFloat(result, "Label");
        Assert.Single(labels);
        Assert.Equal(42f, labels[0]);
    }

    [Fact]
    public void Apply_SingleFeatureVector_ScoresCorrectly()
    {
        // Tree splits on feature[0] <= 5 → left=10, right=20
        var tree = TestHelper.TwoLeafTree(0, 5.0, 10.0, 20.0);
        var ensemble = new TreeEnsemble([tree], bias: 0);
        var transform = new TreeEnsembleScoringTransform(
            ensemble, "Features", TreeEnsembleScoringTransform.OutputMode.Regression);

        // Single-element feature vectors
        var data = TestHelper.Data(
            [new float[] { 3f }, new float[] { 7f }],
            [0f, 1f]);
        var result = transform.Apply(data);

        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(10.0f, scores[0], 1e-5f);  // 3 <= 5 → left=10
        Assert.Equal(20.0f, scores[1], 1e-5f);  // 7 > 5 → right=20
    }
}
