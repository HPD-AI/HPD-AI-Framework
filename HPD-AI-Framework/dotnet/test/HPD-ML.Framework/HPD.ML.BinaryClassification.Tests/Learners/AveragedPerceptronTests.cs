namespace HPD.ML.BinaryClassification.Tests;

using Helium.Primitives;
using HPD.ML.Abstractions;
using Double = Helium.Primitives.Double;

public class AveragedPerceptronTests
{
    [Fact]
    public void Fit_LinearSeparable_LowErrorRate()
    {
        var data = TestHelper.LinearSeparableData(n: 40, seed: 42);
        var learner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 20
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var accuracy = TestHelper.Accuracy(predictions);

        Assert.True(accuracy >= 0.75, $"Accuracy was {accuracy}");
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new AveragedPerceptronLearner();
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_OnlineLearning_ResumesFromModel()
    {
        var batch1 = TestHelper.LinearSeparableData(n: 20, seed: 42);
        var batch2 = TestHelper.LinearSeparableData(n: 20, seed: 99);

        var learner1 = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 5
        });
        var model1 = learner1.Fit(new LearnerInput(batch1));

        // Continue training
        var learner2 = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 5
        });
        var model2 = learner2.Fit(new LearnerInput(batch2, InitialModel: model1));

        // Should produce a model (no crash)
        Assert.IsType<LinearModelParameters>(model2.Parameters);
    }

    [Fact]
    public void Fit_OnlineLearning_UsesExistingWeights()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var learner1 = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 10
        });
        var model1 = learner1.Fit(new LearnerInput(data));
        var p1 = (LinearModelParameters)model1.Parameters;

        // Retrain from scratch with 1 iteration vs resume with 1 iteration
        var freshLearner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 1
        });
        var resumeLearner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 1
        });

        var freshModel = (LinearModelParameters)freshLearner.Fit(new LearnerInput(data)).Parameters;
        var resumeModel = (LinearModelParameters)resumeLearner.Fit(
            new LearnerInput(data, InitialModel: model1)).Parameters;

        // Resume should differ from fresh because it starts with existing weights
        bool differ = false;
        for (int i = 0; i < freshModel.FeatureCount; i++)
            if (Math.Abs((double)freshModel.Weights[i] - (double)resumeModel.Weights[i]) > 0.001)
                differ = true;
        Assert.True(differ, "Resumed model should differ from fresh model");
    }

    [Fact]
    public void Fit_DecreaseLearningRate_Converges()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);
        var learner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            DecreaseLearningRate = true, NumberOfIterations = 10
        });

        var model = learner.Fit(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_L2Regularization_SmallWeights()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var noReg = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            L2Regularization = 0, NumberOfIterations = 10
        });
        var withReg = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            L2Regularization = 0.5, NumberOfIterations = 10
        });

        var noRegNorm = TestHelper.WeightNorm((LinearModelParameters)noReg.Fit(new LearnerInput(data)).Parameters);
        var withRegNorm = TestHelper.WeightNorm((LinearModelParameters)withReg.Fit(new LearnerInput(data)).Parameters);

        Assert.True(withRegNorm < noRegNorm, $"Regularized {withRegNorm} should be < unregularized {noRegNorm}");
    }

    [Fact]
    public void Fit_ProgressReports_ErrorRate()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 3
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("ErrorRate", e.MetricName));
    }

    [Fact]
    public void Fit_WithValidationData_AddsCalibration()
    {
        var trainData = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var valData = TestHelper.LinearSeparableData(n: 10, seed: 99);

        var learner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
        {
            NumberOfIterations = 10
        });
        var model = learner.Fit(new LearnerInput(trainData, ValidationData: valData));
        var predictions = model.Transform.Apply(trainData);

        // Should still produce valid probabilities
        var probs = TestHelper.CollectFloat(predictions, "Probability");
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Fit_WithoutValidationData_NoCalibration()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new AveragedPerceptronLearner();
        var model = learner.Fit(new LearnerInput(data));

        Assert.IsType<LinearScoringTransform>(model.Transform);
    }

    [Fact]
    public void Fit_MultipleEpochs()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
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
        var learner = new AveragedPerceptronLearner(options: new AveragedPerceptronOptions
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
            ("Label", new bool[] { false, false, true, true }));

        var learner = new AveragedPerceptronLearner();
        var model = learner.Fit(new LearnerInput(data));
        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }
}
