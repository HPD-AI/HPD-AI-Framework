namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class SsaChangePointDetectorTests
{
    private static SsaModelParameters TrainParams(int n = 50, int windowSize = 8)
    {
        var data = TestHelper.SineData(n, period: 12);
        var series = SsaTrainer.MaterializeSeries(data, "Value");
        return SsaTrainer.Train(series, windowSize, 0, 0, RankSelectionMethod.Exact);
    }

    [Fact]
    public void ProcessRow_StableData_NoAlert()
    {
        var p = TrainParams(windowSize: 4);
        var detector = new SsaChangePointDetector(p, "Value", threshold: 1e10);
        var data = TestHelper.SineData(40, period: 12);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        // After buffering phase, no alerts on stable data with very high threshold
        Assert.All(alerts, a => Assert.False(a));
    }

    [Fact]
    public void ProcessRow_RegimeChange_AlertFires()
    {
        var p = TrainParams(n: 50, windowSize: 4);
        var detector = new SsaChangePointDetector(p, "Value",
            alerting: AlertingMode.RawScore, threshold: 5);
        // Stable then spike
        var data = TestHelper.SpikeData(40, spikeIndex: 30, baseline: 0f, spikeValue: 100f);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.Contains(true, alerts);
    }

    [Fact]
    public void MartingaleResets_OnAlert()
    {
        var p = TrainParams(windowSize: 4);
        var detector = new SsaChangePointDetector(p, "Value",
            alerting: AlertingMode.RawScore, threshold: 5);

        var state = detector.InitializeState();
        var schema = new HPD.ML.Core.Schema([
            new HPD.ML.Core.Column("Value", HPD.ML.Core.FieldType.Scalar<float>())
        ]);

        // Fill buffer + stable data
        for (int i = 0; i < 10; i++)
        {
            var row = new HPD.ML.Core.DictionaryRow(schema,
                new Dictionary<string, object> { ["Value"] = 0f });
            (state, _) = detector.ProcessRow(state, row);
        }

        // Push spike to trigger alert
        state.LogMartingale = 100; // fake high martingale
        var spikeRow = new HPD.ML.Core.DictionaryRow(schema,
            new Dictionary<string, object> { ["Value"] = 500f });
        (state, _) = detector.ProcessRow(state, spikeRow);

        Assert.Equal(0, state.LogMartingale);
    }

    [Fact]
    public void DefaultAlerting_IsMartingale()
    {
        var p = TrainParams();
        var detector = new SsaChangePointDetector(p, "Value");
        var data = TestHelper.ConstantData(3);
        var schema = detector.GetOutputSchema(data.Schema);
        Assert.NotNull(schema.FindByName("MartingaleScore"));
    }

    [Fact]
    public void Apply_CorrectRowCount()
    {
        var p = TrainParams(windowSize: 4);
        var detector = new SsaChangePointDetector(p, "Value");
        var data = TestHelper.SineData(30);
        var output = detector.Apply(data);
        Assert.Equal(30, TestHelper.CountRows(output));
    }
}
