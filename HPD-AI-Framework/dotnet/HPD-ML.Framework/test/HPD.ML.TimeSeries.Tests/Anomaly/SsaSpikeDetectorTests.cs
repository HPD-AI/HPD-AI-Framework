namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class SsaSpikeDetectorTests
{
    private static SsaModelParameters TrainParams(int n = 50, int windowSize = 8)
    {
        var data = TestHelper.SineData(n, period: 12);
        var series = SsaTrainer.MaterializeSeries(data, "Value");
        return SsaTrainer.Train(series, windowSize, 0, 0, RankSelectionMethod.Exact);
    }

    [Fact]
    public void ProcessRow_BufferingPhase_NoAlert()
    {
        var p = TrainParams(windowSize: 8);
        var detector = new SsaSpikeDetector(p, "Value");
        var data = TestHelper.SineData(7);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.All(alerts, a => Assert.False(a));
        var pValues = TestHelper.CollectFloat(output, "PValue");
        Assert.All(pValues, pv => Assert.Equal(0.5f, pv));
    }

    [Fact]
    public void ProcessRow_AfterBuffering_ProducesScores()
    {
        var p = TrainParams(windowSize: 4);
        var detector = new SsaSpikeDetector(p, "Value");
        var data = TestHelper.SineData(20, period: 12);
        var output = detector.Apply(data);
        var scores = TestHelper.CollectFloat(output, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void ProcessRow_SpikeAfterStable_AlertFires()
    {
        var p = TrainParams(n: 50, windowSize: 4);
        var detector = new SsaSpikeDetector(p, "Value",
            alerting: AlertingMode.RawScore, threshold: 5);
        var data = TestHelper.SpikeData(30, spikeIndex: 25, baseline: 0f, spikeValue: 100f);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.Contains(true, alerts);
    }

    [Fact]
    public void PredictFromAR_ReturnsFinite()
    {
        var p = TrainParams(windowSize: 4);
        var state = new SsaAnomalyState(p, 100);
        // Fill window
        for (int i = 0; i < p.WindowSize; i++)
            state.ObservationWindow.Push(Math.Sin(2 * Math.PI * i / 12));
        double predicted = SsaSpikeDetector.PredictFromAR(state);
        Assert.True(double.IsFinite(predicted));
    }

    [Fact]
    public void UpdateSsaState_UpdatesVector()
    {
        var p = TrainParams(windowSize: 4);
        var state = new SsaAnomalyState(p, 100);
        for (int i = 0; i < p.WindowSize; i++)
            state.ObservationWindow.Push(i + 1.0);
        var before = (double[])state.SsaStateVector.Clone();
        SsaSpikeDetector.UpdateSsaState(state);
        // At least one element should have changed
        bool changed = false;
        for (int i = 0; i < state.SsaStateVector.Length; i++)
            if (Math.Abs(state.SsaStateVector[i] - before[i]) > 1e-15) changed = true;
        Assert.True(changed);
    }

    [Fact]
    public void GetOutputSchema_FourExtraColumns()
    {
        var p = TrainParams();
        var detector = new SsaSpikeDetector(p, "Value");
        var data = TestHelper.SineData(1);
        var outputSchema = detector.GetOutputSchema(data.Schema);
        Assert.NotNull(outputSchema.FindByName("Alert"));
        Assert.NotNull(outputSchema.FindByName("RawScore"));
        Assert.NotNull(outputSchema.FindByName("PValue"));
        Assert.NotNull(outputSchema.FindByName("MartingaleScore"));
    }

    [Fact]
    public void Apply_StreamsThroughAllRows()
    {
        var p = TrainParams(windowSize: 4);
        var detector = new SsaSpikeDetector(p, "Value");
        var data = TestHelper.SineData(20);
        var output = detector.Apply(data);
        Assert.Equal(20, TestHelper.CountRows(output));
    }

    [Fact]
    public void StateSerializer_IsNull()
    {
        var p = TrainParams();
        var detector = new SsaSpikeDetector(p, "Value");
        Assert.Null(detector.StateSerializer);
    }
}
