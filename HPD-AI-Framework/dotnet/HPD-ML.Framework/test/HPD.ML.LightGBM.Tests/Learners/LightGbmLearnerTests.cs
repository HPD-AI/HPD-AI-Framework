namespace HPD.ML.LightGBM.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class LightGbmLearnerTests
{
    [Fact]
    public void Constructor_DefaultColumns()
    {
        var learner = new LightGbmLearner();
        Assert.NotNull(learner);
        Assert.NotNull(learner.Progress);
    }

    [Fact]
    public void Constructor_CustomColumns()
    {
        var learner = new LightGbmLearner(
            labelColumn: "MyLabel",
            featureColumn: "MyFeatures",
            options: new LightGbmOptions { NumberOfIterations = 50 });
        Assert.NotNull(learner);
    }

    [Fact]
    public void GetOutputSchema_MatchesObjective()
    {
        var learner = new LightGbmLearner(options: new LightGbmOptions
        {
            Objective = LightGbmObjective.Binary
        });

        var inputSchema = new SchemaBuilder()
            .AddVectorColumn<float>("Features", 3)
            .Build();

        var outputSchema = learner.GetOutputSchema(inputSchema);
        Assert.NotNull(outputSchema.FindByName("Score"));
        Assert.NotNull(outputSchema.FindByName("Probability"));
        Assert.NotNull(outputSchema.FindByName("PredictedLabel"));
    }
}
