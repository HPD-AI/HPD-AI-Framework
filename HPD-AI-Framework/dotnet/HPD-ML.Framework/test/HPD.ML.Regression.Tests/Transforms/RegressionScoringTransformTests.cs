namespace HPD.ML.Regression.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class RegressionScoringTransformTests
{
    [Fact]
    public void Score_SingleFeature()
    {
        // w=[2], b=1 → score = 2*3 + 1 = 7
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(2)), new Double(1));
        var transform = new RegressionScoringTransform(p, "Features");

        var data = TestHelper.Data(
            ("Features", new float[][] { [3f] }),
            ("Label", new float[] { 0f }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(7f, scores[0], 0.01f);
    }

    [Fact]
    public void Score_MultipleFeatures()
    {
        // w=[1,2], b=0.5 → score = 1*3 + 2*4 + 0.5 = 11.5
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1), new Double(2)), new Double(0.5));
        var transform = new RegressionScoringTransform(p, "Features");

        var data = TestHelper.Data(
            ("Features", new float[][] { [3f, 4f] }),
            ("Label", new float[] { 0f }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(11.5f, scores[0], 0.01f);
    }

    [Fact]
    public void Score_ZeroWeights()
    {
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(0), new Double(0)), new Double(5));
        var transform = new RegressionScoringTransform(p, "Features");

        var data = TestHelper.Data(
            ("Features", new float[][] { [99f, 99f] }),
            ("Label", new float[] { 0f }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(5f, scores[0], 0.01f);
    }

    [Fact]
    public void Score_WithExpApplied()
    {
        // w=[1], b=0, x=2, applyExp=true → exp(2) ≈ 7.389
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new RegressionScoringTransform(p, "Features", applyExp: true);

        var data = TestHelper.Data(
            ("Features", new float[][] { [2f] }),
            ("Label", new float[] { 0f }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal((float)Math.Exp(2), scores[0], 0.01f);
    }

    [Fact]
    public void Score_WithExpApplied_ZeroScore()
    {
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(0)), new Double(0));
        var transform = new RegressionScoringTransform(p, "Features", applyExp: true);

        var data = TestHelper.Data(
            ("Features", new float[][] { [5f] }),
            ("Label", new float[] { 0f }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(1.0f, scores[0], 0.01f);
    }

    [Fact]
    public void Score_PreservesInputColumns()
    {
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new RegressionScoringTransform(p, "Features");

        var data = TestHelper.Data(
            ("Features", new float[][] { [1f] }),
            ("Label", new float[] { 5f }));

        var result = transform.Apply(data);
        Assert.NotNull(result.Schema.FindByName("Features"));
        Assert.NotNull(result.Schema.FindByName("Label"));
        Assert.NotNull(result.Schema.FindByName("Score"));
    }

    [Fact]
    public void GetOutputSchema_AddsScoreColumn()
    {
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new RegressionScoringTransform(p, "Features");
        var inputSchema = new SchemaBuilder().AddColumn<float>("Features").AddColumn<float>("Label").Build();

        var outputSchema = transform.GetOutputSchema(inputSchema);
        Assert.Equal(3, outputSchema.Columns.Count);
        Assert.NotNull(outputSchema.FindByName("Score"));
    }

    [Fact]
    public void Score_MultipleRows()
    {
        // w=[1], b=0 → score = x
        var p = new LinearModelParameters(
            Vector<Double>.FromArray(new Double(1)), new Double(0));
        var transform = new RegressionScoringTransform(p, "Features");

        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new float[] { 0f, 0f, 0f, 0f }));

        var result = transform.Apply(data);
        var scores = TestHelper.CollectFloat(result, "Score");
        Assert.Equal(4, scores.Count);
        Assert.Equal(1f, scores[0], 0.01f);
        Assert.Equal(2f, scores[1], 0.01f);
        Assert.Equal(3f, scores[2], 0.01f);
        Assert.Equal(4f, scores[3], 0.01f);
    }
}
