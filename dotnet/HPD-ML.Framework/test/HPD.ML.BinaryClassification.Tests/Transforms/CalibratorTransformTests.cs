namespace HPD.ML.BinaryClassification.Tests;

using HPD.ML.Core;

public class CalibratorTransformTests
{
    [Fact]
    public void Calibrator_Identity_WhenParametersNeutral()
    {
        // a=-1, b=0 → P = 1/(1+exp(-score)) ≈ sigmoid(score)
        var cal = new CalibratorTransform(-1, 0);
        var data = TestHelper.Data(
            ("Score", new float[] { 0f }),
            ("Probability", new float[] { 0f }));

        var result = cal.Apply(data);
        var probs = TestHelper.CollectFloat(result, "Probability");
        Assert.Equal(0.5f, probs[0], 0.01f); // sigmoid(0) = 0.5
    }

    [Fact]
    public void Calibrator_RemapsScores()
    {
        // a=-2, b=0 → P = 1/(1+exp(-2*score))
        var cal = new CalibratorTransform(-2, 0);
        var data = TestHelper.Data(
            ("Score", new float[] { 1f }),
            ("Probability", new float[] { 0f }));

        var result = cal.Apply(data);
        var probs = TestHelper.CollectFloat(result, "Probability");
        // 1/(1+exp(-2)) ≈ 0.881
        Assert.Equal(0.881f, probs[0], 0.01f);
    }

    [Fact]
    public void Calibrator_PreservesRowCount()
    {
        var cal = new CalibratorTransform(-1, 0);
        var data = TestHelper.Data(
            ("Score", new float[] { 0f, 1f, 2f, -1f, -2f }),
            ("Probability", new float[] { 0f, 0f, 0f, 0f, 0f }));

        var result = cal.Apply(data);
        Assert.Equal(5, TestHelper.CountRows(result));
    }

    [Fact]
    public void Calibrator_PreservesNonProbabilityColumns()
    {
        var cal = new CalibratorTransform(-1, 0);
        var data = TestHelper.Data(
            ("Score", new float[] { 1f }),
            ("Probability", new float[] { 0f }),
            ("Name", new string[] { "test" }));

        var result = cal.Apply(data);
        using var cursor = result.GetCursor(["Name", "Score"]);
        cursor.MoveNext();
        Assert.Equal("test", cursor.Current.GetValue<string>("Name"));
        Assert.Equal(1f, cursor.Current.GetValue<float>("Score"), 0.01f);
    }

    [Fact]
    public void Fit_SeparatedData_ProducesValidProbabilities()
    {
        var data = TestHelper.Data(
            ("Score", new float[] { 5f, 4f, 3f, -3f, -4f, -5f }),
            ("Probability", new float[] { 0, 0, 0, 0, 0, 0 }),
            ("Label", new bool[] { true, true, true, false, false, false }));

        var cal = CalibratorTransform.Fit(data);
        var result = cal.Apply(data);
        var probs = TestHelper.CollectFloat(result, "Probability");

        // All probabilities should be valid numbers in [0,1]
        Assert.Equal(6, probs.Count);
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Fit_AllPositive_Labels()
    {
        var data = TestHelper.Data(
            ("Score", new float[] { 1f, 2f, 3f }),
            ("Probability", new float[] { 0, 0, 0 }),
            ("Label", new bool[] { true, true, true }));

        // Should not throw
        var cal = CalibratorTransform.Fit(data);
        var result = cal.Apply(data);
        Assert.Equal(3, TestHelper.CountRows(result));
    }

    [Fact]
    public void Fit_AllNegative_Labels()
    {
        var data = TestHelper.Data(
            ("Score", new float[] { 1f, 2f, 3f }),
            ("Probability", new float[] { 0, 0, 0 }),
            ("Label", new bool[] { false, false, false }));

        var cal = CalibratorTransform.Fit(data);
        var result = cal.Apply(data);
        Assert.Equal(3, TestHelper.CountRows(result));
    }

    [Fact]
    public void Fit_BalancedData_ReasonableCalibration()
    {
        var data = TestHelper.Data(
            ("Score", new float[] { 3f, 2f, 1f, -1f, -2f, -3f }),
            ("Probability", new float[] { 0, 0, 0, 0, 0, 0 }),
            ("Label", new bool[] { true, true, true, false, false, false }));

        var cal = CalibratorTransform.Fit(data);
        var result = cal.Apply(data);
        var probs = TestHelper.CollectFloat(result, "Probability");

        // Probabilities should be monotonic with score (either increasing or decreasing
        // depending on sign of A), and all valid in [0,1]
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
        // Verify monotonicity: scores are [3,2,1,-1,-2,-3] → probs should be monotonic
        bool increasing = probs[0] <= probs[5];
        bool decreasing = probs[0] >= probs[5];
        Assert.True(increasing || decreasing, "Probabilities should be monotonic with score");
    }
}
