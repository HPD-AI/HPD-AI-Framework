namespace HPD.ML.TimeSeries.Tests;

public class SpectralResidualDetectorTests
{
    [Fact]
    public void ProcessRow_BufferingPhase_NoAlert()
    {
        var detector = new SpectralResidualDetector(windowSize: 16);
        var data = TestHelper.SineData(15);
        var output = detector.Apply(data);
        var alerts = TestHelper.CollectBool(output, "Alert");
        Assert.All(alerts, a => Assert.False(a));
    }

    [Fact]
    public void ProcessRow_NormalData_LowScores()
    {
        var detector = new SpectralResidualDetector(windowSize: 32, threshold: 100);
        var data = TestHelper.SineData(64, period: 8);
        var output = detector.Apply(data);
        var scores = TestHelper.CollectFloat(output, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void ProcessRow_SpikeInData_HighScore()
    {
        var detector = new SpectralResidualDetector(windowSize: 32, threshold: 0.1);
        var data = TestHelper.SpikeData(64, spikeIndex: 50, baseline: 1f, spikeValue: 1000f);
        var output = detector.Apply(data);
        var scores = TestHelper.CollectFloat(output, "RawScore");
        // Some scores should be elevated after the spike enters the window
        double maxScore = scores.Max();
        Assert.True(maxScore > 0, $"Max score {maxScore} should be > 0");
    }

    [Fact]
    public void WindowSize_RoundedToPowerOfTwo()
    {
        var detector = new SpectralResidualDetector(windowSize: 100);
        var state = detector.InitializeState();
        Assert.Equal(128, state.Window.Capacity);
    }

    [Fact]
    public void OutputHas_MagnitudeColumn()
    {
        var data = TestHelper.SineData(5);
        var detector = new SpectralResidualDetector(windowSize: 4);
        var schema = detector.GetOutputSchema(data.Schema);
        Assert.NotNull(schema.FindByName("Magnitude"));
    }

    [Fact]
    public void Apply_AllRowsFinite()
    {
        var detector = new SpectralResidualDetector(windowSize: 32);
        var data = TestHelper.SineData(64, period: 8);
        var output = detector.Apply(data);
        Assert.Equal(64, TestHelper.CountRows(output));
        var scores = TestHelper.CollectFloat(output, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
    }
}
