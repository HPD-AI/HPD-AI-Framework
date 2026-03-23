namespace HPD.ML.BinaryClassification.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class LinearScoringTransformTests
{
    [Fact]
    public void Score_SingleFeature_PositiveWeight()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(2)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [3f] }), ("Label", new bool[] { true }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        var probs = TestHelper.CollectFloat(result, "Probability");
        var preds = TestHelper.CollectBool(result, "PredictedLabel");

        Assert.Equal(6f, scores[0], 0.01f);
        Assert.True(probs[0] > 0.99f); // sigmoid(6) ≈ 0.9975
        Assert.True(preds[0]);
    }

    [Fact]
    public void Score_SingleFeature_NegativeScore()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(-10));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [1f] }), ("Label", new bool[] { false }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");

        Assert.Equal(-9f, scores[0], 0.01f);
        Assert.False(TestHelper.CollectBool(result, "PredictedLabel")[0]);
    }

    [Fact]
    public void Score_MultiFeature_DotProduct()
    {
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1), new Double(2), new Double(3)),
            new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [1f, 1f, 1f] }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(6f, scores[0], 0.01f);
    }

    [Fact]
    public void Score_ZeroWeights_ReturnsBias()
    {
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(0), new Double(0)),
            new Double(1));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [5f, 5f] }));

        var result = transform.Apply(data);
        Assert.Equal(1f, TestHelper.CollectFloat(result, "Score")[0], 0.01f);
    }

    [Fact]
    public void Score_Sigmoid_ZeroLogit_IsHalf()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(0)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [0f] }));

        var result = transform.Apply(data);
        Assert.Equal(0.5f, TestHelper.CollectFloat(result, "Probability")[0], 0.01f);
    }

    [Fact]
    public void Score_Sigmoid_LargePositive_NearOne()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [20f] }));

        var result = transform.Apply(data);
        Assert.True(TestHelper.CollectFloat(result, "Probability")[0] > 0.99f);
    }

    [Fact]
    public void Score_Sigmoid_LargeNegative_NearZero()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [-20f] }));

        var result = transform.Apply(data);
        Assert.True(TestHelper.CollectFloat(result, "Probability")[0] < 0.01f);
    }

    [Fact]
    public void Score_CustomThreshold()
    {
        // Probability ≈ 0.73 (sigmoid(1)) but threshold is 0.8 → false
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features", threshold: 0.8);
        var data = TestHelper.Data(("Features", new float[][] { [1f] }));

        var result = transform.Apply(data);
        Assert.False(TestHelper.CollectBool(result, "PredictedLabel")[0]);
    }

    [Fact]
    public void Score_PreservesInputColumns()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f] }),
            ("Name", new string[] { "Alice" }));

        var result = transform.Apply(data);
        var schema = result.Schema;
        Assert.NotNull(schema.FindByName("Name"));
        Assert.NotNull(schema.FindByName("Features"));
        Assert.NotNull(schema.FindByName("Score"));
        Assert.NotNull(schema.FindByName("Probability"));
        Assert.NotNull(schema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void Score_MultipleRows()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [-5f], [0f], [5f] }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(3, scores.Count);
        Assert.Equal(-5f, scores[0], 0.01f);
        Assert.Equal(0f, scores[1], 0.01f);
        Assert.Equal(5f, scores[2], 0.01f);
    }

    [Fact]
    public void GetOutputSchema_AddsThreeColumns()
    {
        var p = new LinearModelParameters(Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new LinearScoringTransform(p, "Features");
        var inputSchema = new SchemaBuilder().AddColumn<float>("Features").AddColumn<int>("Name").Build();

        var outputSchema = transform.GetOutputSchema(inputSchema);
        Assert.Equal(5, outputSchema.Columns.Count); // Features, Name, Score, Probability, PredictedLabel
    }

    [Fact]
    public void Score_FloatArrayFeatures()
    {
        // w=[1,2], b=1, features=[3,4] → score = 1*3 + 2*4 + 1 = 12
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1), new Double(2)),
            new Double(1));
        var transform = new LinearScoringTransform(p, "Features");
        var data = TestHelper.Data(("Features", new float[][] { [3f, 4f] }));

        var result = transform.Apply(data);
        Assert.Equal(12f, TestHelper.CollectFloat(result, "Score")[0], 0.01f);
    }
}
