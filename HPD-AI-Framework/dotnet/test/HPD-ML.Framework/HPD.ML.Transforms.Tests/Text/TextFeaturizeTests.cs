namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class TextFeaturizeLearnerTests
{
    [Fact]
    public void TextFeat_Tokenize_SplitsOnSeparators()
    {
        var opts = new TextFeaturizeOptions { CaseNormalize = false, RemoveStopWords = false };
        var tokens = TextFeaturizeLearner.Tokenize("hello world!", opts);
        Assert.Equal(["hello", "world"], tokens);
    }

    [Fact]
    public void TextFeat_Tokenize_CaseNormalize()
    {
        var opts = new TextFeaturizeOptions { CaseNormalize = true, RemoveStopWords = false };
        var tokens = TextFeaturizeLearner.Tokenize("Hello World", opts);
        Assert.Equal(["hello", "world"], tokens);
    }

    [Fact]
    public void TextFeat_StopWords_Removed()
    {
        var opts = new TextFeaturizeOptions { CaseNormalize = true, RemoveStopWords = true, NgramMin = 1, NgramMax = 1 };
        var tokens = TextFeaturizeLearner.Tokenize("the cat is here", opts);
        var ngrams = TextFeaturizeLearner.ExtractNgrams(tokens, opts).ToList();
        Assert.Contains("cat", ngrams);
        Assert.Contains("here", ngrams);
        Assert.DoesNotContain("the", ngrams);
        Assert.DoesNotContain("is", ngrams);
    }

    [Fact]
    public void TextFeat_Ngrams_Extracted()
    {
        var opts = new TextFeaturizeOptions { CaseNormalize = false, RemoveStopWords = false, NgramMin = 1, NgramMax = 2 };
        var tokens = new[] { "cat", "sat" };
        var ngrams = TextFeaturizeLearner.ExtractNgrams(tokens, opts).ToList();
        Assert.Contains("cat", ngrams);
        Assert.Contains("sat", ngrams);
        Assert.Contains("cat|sat", ngrams);
        Assert.Equal(3, ngrams.Count);
    }

    [Fact]
    public void TextFeat_Fit_BuildsVocabulary()
    {
        var data = TestHelper.Data(("Text", new string[] {
            "the cat sat",
            "the dog ran",
            "cat and dog"
        }));
        var learner = new TextFeaturizeLearner("Text", options: new TextFeaturizeOptions
        {
            NgramMin = 1, NgramMax = 1, MaxFeatures = 100
        });
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<TextFeaturizeParameters>(model.Parameters);
        Assert.True(p.FeatureIndex.Count > 0);
        Assert.True(p.IdfWeights.Length > 0);
    }

    [Fact]
    public void TextFeat_Transform_ProducesTfIdf()
    {
        var data = TestHelper.Data(("Text", new string[] {
            "cat dog cat",
            "dog bird"
        }));
        var learner = new TextFeaturizeLearner("Text", options: new TextFeaturizeOptions
        {
            NgramMin = 1, NgramMax = 1, MaxFeatures = 100, RemoveStopWords = false
        });
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var vectors = TestHelper.CollectFloatArray(result, "Text_Features");
        Assert.Equal(2, vectors.Count);
        // At least some non-zero values in first vector (has "cat" twice)
        Assert.True(vectors[0].Any(v => v > 0));
    }

    [Fact]
    public void TextFeat_OutputColumn_DefaultName()
    {
        var schema = new SchemaBuilder().AddColumn("Review", new FieldType(typeof(string))).Build();
        var learner = new TextFeaturizeLearner("Review");
        var outSchema = learner.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("Review_Features"));
    }
}

public class TextFeaturizeTransformTests
{
    [Fact]
    public void TextTransform_VectorLength_MatchesFeatures()
    {
        var featureIndex = new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 2, ["d"] = 3, ["e"] = 4 };
        var idf = new double[] { 1, 1, 1, 1, 1 };
        var transform = new TextFeaturizeTransform("T", "T_F", featureIndex, idf, new TextFeaturizeOptions());
        var schema = new SchemaBuilder().AddColumn("T", new FieldType(typeof(string))).Build();
        var outSchema = transform.GetOutputSchema(schema);
        var col = outSchema.FindByName("T_F")!;
        Assert.True(col.Type.IsVector);
        Assert.Equal(5, col.Type.Dimensions![0]);
    }

    [Fact]
    public void TextTransform_UnknownTerm_ZeroWeight()
    {
        var featureIndex = new Dictionary<string, int> { ["known"] = 0 };
        var idf = new double[] { 1.0 };
        var opts = new TextFeaturizeOptions { CaseNormalize = true, RemoveStopWords = false, NgramMin = 1, NgramMax = 1 };
        var transform = new TextFeaturizeTransform("T", "T_F", featureIndex, idf, opts);
        var data = TestHelper.Data(("T", new string[] { "unknown words only" }));
        var result = transform.Apply(data);
        var vectors = TestHelper.CollectFloatArray(result, "T_F");
        Assert.All(vectors[0], v => Assert.Equal(0f, v));
    }

    [Fact]
    public void TextTransform_PreservesRowCount()
    {
        var featureIndex = new Dictionary<string, int> { ["a"] = 0 };
        var transform = new TextFeaturizeTransform("T", "T_F", featureIndex, [1.0], new TextFeaturizeOptions());
        Assert.True(transform.Properties.PreservesRowCount);
    }
}
