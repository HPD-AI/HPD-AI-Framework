namespace HPD.ML.LightGBM.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class LightGbmExtensionsTests
{
    [Fact]
    public void LightGbm_DefaultOptions_CreatesLearner()
    {
        var learner = ILearner.LightGbm();
        Assert.NotNull(learner);
        Assert.IsType<LightGbmLearner>(learner);
    }

    [Fact]
    public void LightGbmBinaryClassification_SetsObjective()
    {
        var learner = ILearner.LightGbmBinaryClassification();
        Assert.IsType<LightGbmLearner>(learner);
        // Verify via output schema — binary mode produces Score + Probability + PredictedLabel
        var schema = new SchemaBuilder()
            .AddVectorColumn<float>("Features", 2)
            .Build();
        var outputSchema = learner.GetOutputSchema(schema);
        Assert.NotNull(outputSchema.FindByName("Probability"));
        Assert.NotNull(outputSchema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void LightGbmRegression_SetsObjective()
    {
        var learner = ILearner.LightGbmRegression();
        Assert.IsType<LightGbmLearner>(learner);
        var schema = new SchemaBuilder()
            .AddVectorColumn<float>("Features", 2)
            .Build();
        var outputSchema = learner.GetOutputSchema(schema);
        Assert.NotNull(outputSchema.FindByName("Score"));
        Assert.Null(outputSchema.FindByName("Probability"));
    }

    [Fact]
    public void LightGbmMulticlass_SetsObjectiveAndClasses()
    {
        var learner = ILearner.LightGbmMulticlass(5);
        Assert.IsType<LightGbmLearner>(learner);
        var schema = new SchemaBuilder()
            .AddVectorColumn<float>("Features", 2)
            .Build();
        var outputSchema = learner.GetOutputSchema(schema);
        var scoreCol = outputSchema.FindByName("Score");
        Assert.NotNull(scoreCol);
        Assert.True(scoreCol!.Type.IsVector);
        Assert.NotNull(outputSchema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void LightGbmRanking_SetsObjective()
    {
        var learner = ILearner.LightGbmRanking();
        Assert.IsType<LightGbmLearner>(learner);
        var schema = new SchemaBuilder()
            .AddVectorColumn<float>("Features", 2)
            .Build();
        var outputSchema = learner.GetOutputSchema(schema);
        Assert.NotNull(outputSchema.FindByName("Score"));
        Assert.Null(outputSchema.FindByName("Probability"));
    }
}
