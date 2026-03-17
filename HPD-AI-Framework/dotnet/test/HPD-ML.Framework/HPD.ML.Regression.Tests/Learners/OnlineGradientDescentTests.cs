namespace HPD.ML.Regression.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class OnlineGradientDescentTests
{
    [Fact]
    public void Fit_LinearData_LowMSE()
    {
        var data = TestHelper.LinearData(slope: 2, intercept: 1, noise: 0.1, n: 30, seed: 42);
        var learner = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 10, LearningRate = 0.01
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var mse = TestHelper.MSE(predictions);

        Assert.True(mse < 2.0, $"MSE was {mse}");
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OnlineGradientDescentLearner();
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_AverageWeights_ProducesModel()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);

        var avg = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            AverageWeights = true, NumberOfIterations = 5, LearningRate = 0.01
        });
        var noAvg = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            AverageWeights = false, NumberOfIterations = 5, LearningRate = 0.01
        });

        var avgModel = avg.Fit(new LearnerInput(data));
        var noAvgModel = noAvg.Fit(new LearnerInput(data));

        Assert.NotNull(avgModel);
        Assert.NotNull(noAvgModel);
    }

    [Fact]
    public void Fit_NoAveraging_UsesLastWeights()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);

        var avg = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            AverageWeights = true, NumberOfIterations = 5, LearningRate = 0.01
        });
        var noAvg = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            AverageWeights = false, NumberOfIterations = 5, LearningRate = 0.01
        });

        var p1 = (LinearModelParameters)avg.Fit(new LearnerInput(data)).Parameters;
        var p2 = (LinearModelParameters)noAvg.Fit(new LearnerInput(data)).Parameters;

        // Should differ because averaging produces different weights
        bool differ = false;
        for (int i = 0; i < p1.FeatureCount; i++)
            if (Math.Abs((double)p1.Weights[i] - (double)p2.Weights[i]) > 0.001)
                differ = true;
        Assert.True(differ, "Averaged and non-averaged models should differ");
    }

    [Fact]
    public void Fit_OnlineLearning_ResumesFromModel()
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

        Assert.IsType<LinearModelParameters>(model2.Parameters);
    }

    [Fact]
    public void Fit_OnlineLearning_UsesExistingWeights()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);

        var learner1 = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 10, LearningRate = 0.01
        });
        var model1 = learner1.Fit(new LearnerInput(data));

        var fresh = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 1, LearningRate = 0.01
        });
        var resume = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 1, LearningRate = 0.01
        });

        var freshModel = (LinearModelParameters)fresh.Fit(new LearnerInput(data)).Parameters;
        var resumeModel = (LinearModelParameters)resume.Fit(
            new LearnerInput(data, InitialModel: model1)).Parameters;

        bool differ = false;
        for (int i = 0; i < freshModel.FeatureCount; i++)
            if (Math.Abs((double)freshModel.Weights[i] - (double)resumeModel.Weights[i]) > 0.001)
                differ = true;
        Assert.True(differ, "Resumed model should differ from fresh model");
    }

    [Fact]
    public void Fit_DecreaseLearningRate_Converges()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);
        var learner = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            DecreaseLearningRate = true, NumberOfIterations = 10, LearningRate = 0.1
        });

        var model = learner.Fit(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_L2Regularization_SmallWeights()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);

        var noReg = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            L2Regularization = 0, NumberOfIterations = 10, LearningRate = 0.01
        });
        var withReg = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            L2Regularization = 0.5, NumberOfIterations = 10, LearningRate = 0.01
        });

        var noRegNorm = TestHelper.WeightNorm((LinearModelParameters)noReg.Fit(new LearnerInput(data)).Parameters);
        var withRegNorm = TestHelper.WeightNorm((LinearModelParameters)withReg.Fit(new LearnerInput(data)).Parameters);

        Assert.True(withRegNorm < noRegNorm, $"Regularized {withRegNorm} should be < unregularized {noRegNorm}");
    }

    [Fact]
    public void Fit_ProgressReports_SquaredLoss()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 3
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("SquaredLoss", e.MetricName));
    }

    [Fact]
    public void Fit_MultiplePasses()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 5
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.Equal(5, events.Count);
    }

    [Fact]
    public async Task FitAsync_Works()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OnlineGradientDescentLearner(options: new OnlineGradientDescentOptions
        {
            NumberOfIterations = 3
        });
        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_SingleFeature()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new float[] { 2f, 4f, 6f, 8f }));

        var learner = new OnlineGradientDescentLearner();
        var model = learner.Fit(new LearnerInput(data));
        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }
}
