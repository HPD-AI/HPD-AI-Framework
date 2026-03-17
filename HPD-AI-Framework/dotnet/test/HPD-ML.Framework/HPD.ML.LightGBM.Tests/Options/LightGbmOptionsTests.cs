namespace HPD.ML.LightGBM.Tests;

public class LightGbmOptionsTests
{
    [Fact]
    public void ToParameterString_DefaultOptions_ContainsDefaults()
    {
        var options = new LightGbmOptions();
        string param = options.ToParameterString();

        Assert.Contains("objective=regression", param);
        Assert.Contains("num_iterations=100", param);
        Assert.Contains("learning_rate=0.1", param);
        Assert.Contains("boosting_type=gbdt", param);
        Assert.Contains("num_leaves=31", param);
        Assert.Contains("verbosity=-1", param);
    }

    [Fact]
    public void ToParameterString_BinaryObjective_CorrectString()
    {
        var options = new LightGbmOptions { Objective = LightGbmObjective.Binary };
        string param = options.ToParameterString();
        Assert.Contains("objective=binary", param);
    }

    [Fact]
    public void ToParameterString_Multiclass_IncludesNumClass()
    {
        var options = new LightGbmOptions
        {
            Objective = LightGbmObjective.Multiclass,
            NumberOfClasses = 5
        };
        string param = options.ToParameterString();
        Assert.Contains("objective=multiclass", param);
        Assert.Contains("num_class=5", param);
    }

    [Fact]
    public void ToParameterString_Ranking_IncludesNdcgMetric()
    {
        var options = new LightGbmOptions { Objective = LightGbmObjective.Ranking };
        string param = options.ToParameterString();
        Assert.Contains("objective=lambdarank", param);
        Assert.Contains("metric=ndcg", param);
    }

    [Fact]
    public void ToParameterString_Tweedie_IncludesVariancePower()
    {
        var options = new LightGbmOptions
        {
            Objective = LightGbmObjective.Tweedie,
            TweedieVariancePower = 1.8
        };
        string param = options.ToParameterString();
        Assert.Contains("objective=tweedie", param);
        Assert.Contains("tweedie_variance_power=1.8", param);
    }

    [Fact]
    public void ToParameterString_InvariantCulture_DecimalDot()
    {
        var options = new LightGbmOptions { LearningRate = 0.05 };
        string param = options.ToParameterString();
        // Must use dot, not comma, regardless of locale
        Assert.Contains("learning_rate=0.05", param);
        Assert.DoesNotContain("learning_rate=0,05", param);
    }
}
