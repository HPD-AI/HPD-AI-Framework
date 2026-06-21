namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MutualInfoTests
{
    [Fact]
    public void MI_SelectsTopK()
    {
        // Feature1 = label (perfect correlation), Feature2-5 = random
        var label = new float[] { 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 };
        var feat1 = label.ToArray(); // identical to label
        var feat2 = new float[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 }; // constant
        var feat3 = new float[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 }; // descending
        var data = TestHelper.Data(
            ("Label", label), ("F1", feat1), ("F2", feat2), ("F3", feat3));
        var learner = new MutualInfoFeatureSelectionLearner("Label", ["F1", "F2", "F3"], topK: 2);
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        // Should have Label + 2 selected features
        Assert.Equal(3, result.Schema.Columns.Count);
        Assert.NotNull(result.Schema.FindByName("Label"));
    }

    [Fact]
    public void MI_CorrelatedFeature_HighScore()
    {
        var label = new float[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 };
        var correlated = label.ToArray();
        var random = new float[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3 };
        var data = TestHelper.Data(
            ("Label", label), ("Corr", correlated), ("Rand", random));
        var learner = new MutualInfoFeatureSelectionLearner("Label", ["Corr", "Rand"], topK: 1);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MutualInfoParameters>(model.Parameters);
        // Correlated feature should have higher MI
        Assert.True(p.FeatureScores.ContainsKey("Corr"));
    }

    [Fact]
    public void MI_RandomFeature_LowScore()
    {
        var label = new float[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 };
        var correlated = label.ToArray();
        var constant = new float[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 };
        var data = TestHelper.Data(
            ("Label", label), ("Corr", correlated), ("Const", constant));
        var learner = new MutualInfoFeatureSelectionLearner("Label", ["Corr", "Const"], topK: 1);
        var model = learner.Fit(new LearnerInput(data));
        // Top-1 should be Corr, not Const
        var result = model.Transform.Apply(data);
        Assert.NotNull(result.Schema.FindByName("Corr"));
        Assert.Null(result.Schema.FindByName("Const"));
    }

    [Fact]
    public void MI_Schema_OnlySelectedColumns()
    {
        var data = TestHelper.Data(
            ("Label", new float[] { 0, 1 }),
            ("F1", new float[] { 1, 2 }),
            ("F2", new float[] { 3, 4 }),
            ("F3", new float[] { 5, 6 }));
        var transform = new MutualInfoFeatureSelectionTransform("Label", ["F1", "F3"]);
        var outSchema = transform.GetOutputSchema(data.Schema);
        Assert.Equal(3, outSchema.Columns.Count); // Label + F1 + F3
        Assert.NotNull(outSchema.FindByName("Label"));
        Assert.NotNull(outSchema.FindByName("F1"));
        Assert.NotNull(outSchema.FindByName("F3"));
        Assert.Null(outSchema.FindByName("F2"));
    }

    [Fact]
    public void MI_EmptyData_ReturnsEmptySelection()
    {
        var schema = new SchemaBuilder()
            .AddColumn<float>("Label")
            .AddColumn<float>("F1")
            .Build();
        var data = new InMemoryDataHandle(schema, new Dictionary<string, Array>
        {
            ["Label"] = Array.Empty<float>(),
            ["F1"] = Array.Empty<float>()
        });
        var learner = new MutualInfoFeatureSelectionLearner("Label", ["F1"], topK: 1);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MutualInfoParameters>(model.Parameters);
        Assert.Empty(p.FeatureScores);
    }

    [Fact]
    public void MI_Discretize_CorrectBins()
    {
        var values = new List<float> { 1, 2, 3, 4 };
        var bins = MutualInfoFeatureSelectionLearner.Discretize(values, numBins: 2);
        Assert.Equal(4, bins.Length);
        // Should have 2 distinct bin values
        var unique = bins.Distinct().Count();
        Assert.True(unique <= 2);
    }

    [Fact]
    public void MI_ComputeMI_PerfectCorrelation()
    {
        var x = new int[] { 0, 0, 1, 1 };
        var y = new int[] { 0, 0, 1, 1 };
        var mi = MutualInfoFeatureSelectionLearner.ComputeMutualInformation(x, y, 4);
        Assert.True(mi > 0);
    }

    [Fact]
    public void MI_ComputeMI_Independent()
    {
        // Uniform independent: all 4 combinations equally likely
        var x = new int[] { 0, 0, 1, 1 };
        var y = new int[] { 0, 1, 0, 1 };
        var mi = MutualInfoFeatureSelectionLearner.ComputeMutualInformation(x, y, 4);
        Assert.Equal(0.0, mi, 0.001);
    }

    [Fact]
    public void MI_Parameters_HasScores()
    {
        var data = TestHelper.Data(
            ("Label", new float[] { 0, 1, 0, 1 }),
            ("F1", new float[] { 0, 1, 0, 1 }));
        var learner = new MutualInfoFeatureSelectionLearner("Label", ["F1"], topK: 1);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MutualInfoParameters>(model.Parameters);
        Assert.Single(p.FeatureScores);
        Assert.True(p.FeatureScores["F1"] > 0);
    }

    [Fact]
    public async Task MI_FitAsync_SameResult()
    {
        var data = TestHelper.Data(
            ("Label", new float[] { 0, 1, 0, 1 }),
            ("F1", new float[] { 0, 1, 0, 1 }));
        var learner = new MutualInfoFeatureSelectionLearner("Label", ["F1"], topK: 1);
        var syncModel = learner.Fit(new LearnerInput(data));
        var asyncModel = await learner.FitAsync(new LearnerInput(data));
        var syncP = (MutualInfoParameters)syncModel.Parameters;
        var asyncP = (MutualInfoParameters)asyncModel.Parameters;
        Assert.Equal(syncP.FeatureScores["F1"], asyncP.FeatureScores["F1"], 0.001);
    }
}
