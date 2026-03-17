namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class PipelineTests
{
    [Fact]
    public void Pipeline_NormalizeThenEncode()
    {
        var data = TestHelper.Data(
            ("Age", new float[] { 20, 40, 60 }),
            ("City", new string[] { "A", "B", "A" }));

        // Fit normalizer
        var normLearner = new MinMaxNormalizeLearner("Age");
        var normModel = normLearner.Fit(new LearnerInput(data));

        // Fit encoder
        var encLearner = new OneHotEncodeLearner("City");
        var encModel = encLearner.Fit(new LearnerInput(data));

        // Compose
        var pipeline = TransformComposer.Compose(normModel.Transform, encModel.Transform);
        var result = pipeline.Apply(data);

        var ages = TestHelper.CollectFloat(result, "Age");
        Assert.Equal(0f, ages[0], 0.01f);
        Assert.Equal(0.5f, ages[1], 0.01f);
        Assert.Equal(1f, ages[2], 0.01f);

        var cities = TestHelper.CollectFloatArray(result, "City");
        Assert.Equal(3, cities.Count);
    }

    [Fact]
    public void Pipeline_DropMissing_ThenNormalize()
    {
        var data = TestHelper.Data(("V", new float[] { 10, float.NaN, 30, float.NaN, 50 }));

        var drop = new MissingValueDropTransform("V");
        var cleaned = drop.Apply(data);

        var normLearner = new MinMaxNormalizeLearner("V");
        var normModel = normLearner.Fit(new LearnerInput(cleaned));
        var result = normModel.Transform.Apply(cleaned);

        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(3, values.Count);
        Assert.Equal(0f, values[0], 0.01f);  // 10 → min
        Assert.Equal(0.5f, values[1], 0.01f); // 30 → mid
        Assert.Equal(1f, values[2], 0.01f);   // 50 → max
    }

    [Fact]
    public void Pipeline_FitLearner_ApplyToTestData()
    {
        var trainData = TestHelper.Data(("V", new float[] { 0, 50, 100 }));
        var testData = TestHelper.Data(("V", new float[] { 25, 75 }));

        var learner = new MinMaxNormalizeLearner("V");
        var model = learner.Fit(new LearnerInput(trainData));

        // Apply train-fitted transform to test data
        var result = model.Transform.Apply(testData);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(0.25f, values[0], 0.01f);
        Assert.Equal(0.75f, values[1], 0.01f);
    }

    [Fact]
    public void Pipeline_TextFeaturize_EndToEnd()
    {
        var data = TestHelper.Data(("Text", new string[] {
            "cat sat mat",
            "dog ran fast",
            "cat dog bird"
        }));

        var learner = new TextFeaturizeLearner("Text", options: new TextFeaturizeOptions
        {
            NgramMin = 1, NgramMax = 1, MaxFeatures = 50, RemoveStopWords = false
        });
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);

        var vectors = TestHelper.CollectFloatArray(result, "Text_Features");
        Assert.Equal(3, vectors.Count);
        Assert.All(vectors, v => Assert.True(v.Length > 0));
    }

    [Fact]
    public void Pipeline_V2K_OneHot_Compose()
    {
        var mapping = new Dictionary<string, int> { ["X"] = 0, ["Y"] = 1, ["Z"] = 2 };
        var data = TestHelper.Data(("Cat", new string[] { "X", "Y", "Z", "X" }));

        var v2k = new ValueToKeyTransform("Cat", mapping);
        var keyed = v2k.Apply(data);

        // Verify keys
        var keys = TestHelper.CollectInt(keyed, "Cat");
        Assert.Equal([0, 1, 2, 0], keys);
    }
}
