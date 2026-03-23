namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;

public class ExtensionTests
{
    [Fact]
    public void Ext_ILearner_MinMaxNormalize()
    {
        var learner = ILearner.MinMaxNormalize("V");
        Assert.IsType<MinMaxNormalizeLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_MeanVarianceNormalize()
    {
        var learner = ILearner.MeanVarianceNormalize("V");
        Assert.IsType<MeanVarianceNormalizeLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_OneHotEncode()
    {
        var learner = ILearner.OneHotEncode("V");
        Assert.IsType<OneHotEncodeLearner>(learner);
    }

    [Fact]
    public void Ext_ITransform_DropMissing()
    {
        var transform = ITransform.DropMissing("V");
        Assert.IsType<MissingValueDropTransform>(transform);
    }

    [Fact]
    public void Ext_ITransform_Hash()
    {
        var transform = ITransform.Hash("V");
        Assert.IsType<HashTransform>(transform);
    }

    [Fact]
    public void Ext_ILearner_MutualInfoFS()
    {
        var learner = ILearner.MutualInfoFeatureSelection("Label", ["F1", "F2"], topK: 1);
        Assert.IsType<MutualInfoFeatureSelectionLearner>(learner);
    }
}
