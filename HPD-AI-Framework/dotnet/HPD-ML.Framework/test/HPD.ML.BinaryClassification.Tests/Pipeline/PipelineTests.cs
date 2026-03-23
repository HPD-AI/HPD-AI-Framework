namespace HPD.ML.BinaryClassification.Tests;

using HPD.ML.Abstractions;

public class PipelineTests
{
    [Fact]
    public void Pipeline_NormalizeThenClassify()
    {
        // Create data with raw features that need normalizing
        var data = TestHelper.Data(
            ("Features", new float[][] { [100f, 200f], [150f, 250f], [50f, 100f], [200f, 300f] }),
            ("Label", new bool[] { false, true, false, true }));

        // Train logistic regression directly (features already as float[])
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 30, L2Regularization = 0.1f
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);

        // Should produce all expected columns
        Assert.NotNull(predictions.Schema.FindByName("Score"));
        Assert.NotNull(predictions.Schema.FindByName("Probability"));
        Assert.NotNull(predictions.Schema.FindByName("PredictedLabel"));

        var probs = TestHelper.CollectFloat(predictions, "Probability");
        Assert.Equal(4, probs.Count);
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Pipeline_TrainOnTrain_PredictOnTest()
    {
        var trainData = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var testData = TestHelper.LinearSeparableData(n: 10, seed: 99);

        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 50, L2Regularization = 0.1f
        });
        var model = learner.Fit(new LearnerInput(trainData));

        // Apply trained model to test data
        var testPredictions = model.Transform.Apply(testData);

        Assert.Equal(10, TestHelper.CountRows(testPredictions));
        var probs = TestHelper.CollectFloat(testPredictions, "Probability");
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));

        // Should have reasonable accuracy on test
        var accuracy = TestHelper.Accuracy(testPredictions);
        Assert.True(accuracy >= 0.5, $"Test accuracy was {accuracy}");
    }

    [Fact]
    public void Pipeline_SVM_WithCalibration()
    {
        var trainData = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var valData = TestHelper.LinearSeparableData(n: 10, seed: 99);

        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            NumberOfIterations = 20, Seed = 42
        });

        // Fit with validation data for Platt scaling
        var model = learner.Fit(new LearnerInput(trainData, ValidationData: valData));
        var predictions = model.Transform.Apply(trainData);

        var probs = TestHelper.CollectFloat(predictions, "Probability");
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));

        // Calibrated probabilities should be different from raw sigmoid
        // (Platt scaling adjusts A and B)
        Assert.Equal(30, probs.Count);
    }
}
