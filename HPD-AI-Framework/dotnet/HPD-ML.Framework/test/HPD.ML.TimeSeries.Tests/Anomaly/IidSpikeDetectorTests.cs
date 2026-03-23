namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class IidSpikeDetectorTests
{
    [Fact]
    public void ProcessRow_NormalData_NoAlert()
    {
        // Use RawScore alerting with very high threshold so normal data never fires
        var data = TestHelper.SineData(50, period: 12, amplitude: 1);
        var detector = new IidSpikeDetector(
            alerting: AlertingMode.RawScore, threshold: 1000);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.All(alerts, a => Assert.False(a));
    }

    [Fact]
    public void ProcessRow_SpikeInData_AlertFires()
    {
        var data = TestHelper.SpikeData(50, spikeIndex: 40, baseline: 10f, spikeValue: 1000f);
        var detector = new IidSpikeDetector(
            alerting: AlertingMode.RawScore, threshold: 50);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.Contains(true, alerts);
    }

    [Fact]
    public void ProcessRow_OutputHasCorrectColumns()
    {
        var data = TestHelper.ConstantData(5);
        var detector = new IidSpikeDetector();
        var output = detector.Apply(data);
        var schema = output.Schema;
        Assert.NotNull(schema.FindByName("Alert"));
        Assert.NotNull(schema.FindByName("RawScore"));
        Assert.NotNull(schema.FindByName("PValue"));
    }

    [Fact]
    public void ProcessRow_PreservesInputColumns()
    {
        var data = TestHelper.ConstantData(5, 7f);
        var detector = new IidSpikeDetector();
        var output = detector.Apply(data);
        var values = TestHelper.CollectFloat(output, "Value");
        Assert.All(values, v => Assert.Equal(7f, v));
    }

    [Fact]
    public void GetOutputSchema_AddsThreeColumns()
    {
        var data = TestHelper.ConstantData(1);
        var detector = new IidSpikeDetector();
        var outputSchema = detector.GetOutputSchema(data.Schema);
        // Input has "Value", output adds Alert, RawScore, PValue = 4 total
        Assert.Equal(4, outputSchema.Columns.Count);
    }

    [Fact]
    public void Apply_ReturnsDataHandle()
    {
        var data = TestHelper.SineData(20);
        var detector = new IidSpikeDetector();
        var output = detector.Apply(data);
        Assert.Equal(20, TestHelper.CountRows(output));
        var scores = TestHelper.CollectFloat(output, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void Properties_IsStateful()
    {
        var detector = new IidSpikeDetector();
        Assert.True(detector.Properties.IsStateful);
        Assert.True(detector.Properties.RequiresOrdering);
        Assert.True(detector.Properties.PreservesRowCount);
    }
}
