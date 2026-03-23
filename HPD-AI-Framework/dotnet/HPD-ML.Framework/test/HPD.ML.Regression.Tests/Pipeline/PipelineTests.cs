namespace HPD.ML.Regression.Tests;

using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;

public class PipelineTests
{
    [Fact]
    public void Pipeline_TrainOnTrain_PredictOnTest()
    {
        var trainData = TestHelper.LinearData(slope: 2, intercept: 1, n: 30, seed: 42);
        var testData = TestHelper.LinearData(slope: 2, intercept: 1, n: 10, seed: 99);

        var learner = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            MaxIterations = 50, L2Regularization = 0.01f
        });
        var model = learner.Fit(new LearnerInput(trainData));
        var testPredictions = model.Transform.Apply(testData);

        Assert.Equal(10, TestHelper.CountRows(testPredictions));
        var scores = TestHelper.CollectFloat(testPredictions, "Score");
        Assert.All(scores, s => Assert.True(float.IsFinite(s), $"Score {s} should be finite"));
    }

    [Fact]
    public void Pipeline_Poisson_CountPredictions()
    {
        var data = TestHelper.CountData(n: 30, seed: 42);
        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions
        {
            MaxIterations = 50, L2Regularization = 0.1f
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var scores = TestHelper.CollectFloat(predictions, "Score");

        Assert.All(scores, s => Assert.True(s > 0, $"Poisson prediction {s} should be > 0"));
    }

    [Fact]
    public void Pipeline_OnlineGD_StreamingUpdate()
    {
        var batch1 = TestHelper.LinearData(n: 20, seed: 42);
        var batch2 = TestHelper.LinearData(n: 20, seed: 99);

        var learner1 = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 5, LearningRate = 0.01
        });
        var model1 = learner1.Fit(new LearnerInput(batch1));

        var learner2 = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 5, LearningRate = 0.01
        });
        var model2 = learner2.Fit(new LearnerInput(batch2, InitialModel: model1));

        var predictions = model2.Transform.Apply(batch2);
        var scores = TestHelper.CollectFloat(predictions, "Score");
        Assert.Equal(20, scores.Count);
        Assert.All(scores, s => Assert.True(float.IsFinite(s), $"Score {s} should be finite"));
    }
}
