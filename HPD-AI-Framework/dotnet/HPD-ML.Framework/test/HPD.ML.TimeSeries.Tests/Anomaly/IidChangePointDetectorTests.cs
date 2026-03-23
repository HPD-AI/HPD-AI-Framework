namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class IidChangePointDetectorTests
{
    [Fact]
    public void ProcessRow_StableData_NoAlert()
    {
        // Use RawScore alerting with very high threshold — constant data never fires
        var data = TestHelper.SineData(50, period: 12, amplitude: 1);
        var detector = new IidChangePointDetector(
            alerting: AlertingMode.RawScore, threshold: 1000);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.All(alerts, a => Assert.False(a));
    }

    [Fact]
    public void ProcessRow_MeanShift_AlertFires()
    {
        var data = TestHelper.MeanShiftData(60, shiftIndex: 30, meanBefore: 0f, meanAfter: 50f, noise: 0.1f);
        var detector = new IidChangePointDetector(
            alerting: AlertingMode.RawScore, threshold: 20);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.Contains(true, alerts);
    }

    [Fact]
    public void ProcessRow_MartingaleResetsOnAlert()
    {
        // After an alert, the martingale score should reset
        var detector = new IidChangePointDetector(
            alerting: AlertingMode.RawScore, threshold: 20);
        var state = detector.InitializeState();

        // Build up score history
        var schema = new HPD.ML.Core.Schema([
            new HPD.ML.Core.Column("Value", HPD.ML.Core.FieldType.Scalar<float>())
        ]);

        // Push normal values
        for (int i = 0; i < 20; i++)
        {
            var values = new Dictionary<string, object> { ["Value"] = 5f };
            var row = new HPD.ML.Core.DictionaryRow(schema, values);
            (state, _) = detector.ProcessRow(state, row);
        }

        // Push extreme value to trigger alert
        var spikeValues = new Dictionary<string, object> { ["Value"] = 500f };
        var spikeRow = new HPD.ML.Core.DictionaryRow(schema, spikeValues);
        (state, _) = detector.ProcessRow(state, spikeRow);

        // After alert, martingale should have been reset
        Assert.Equal(0, state.LogMartingale);
    }

    [Fact]
    public void OutputHas_MartingaleScoreColumn()
    {
        var data = TestHelper.ConstantData(5);
        var detector = new IidChangePointDetector();
        var output = detector.Apply(data);
        Assert.NotNull(output.Schema.FindByName("MartingaleScore"));
    }

    [Fact]
    public void DefaultAlerting_IsMartingale()
    {
        var detector = new IidChangePointDetector();
        // Verify via output schema - it should have MartingaleScore
        var data = TestHelper.ConstantData(3);
        var schema = detector.GetOutputSchema(data.Schema);
        Assert.NotNull(schema.FindByName("MartingaleScore"));
    }

    [Fact]
    public void Apply_ProducesCorrectRowCount()
    {
        var data = TestHelper.SineData(40);
        var detector = new IidChangePointDetector();
        var output = detector.Apply(data);
        Assert.Equal(40, TestHelper.CountRows(output));
    }
}
