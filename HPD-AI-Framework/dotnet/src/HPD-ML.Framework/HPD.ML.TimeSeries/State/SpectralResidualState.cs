namespace HPD.ML.TimeSeries;

/// <summary>
/// State for spectral residual anomaly detection. Just a sliding window.
/// </summary>
public sealed class SpectralResidualState
{
    public SlidingWindow<double> Window { get; }
    public long RowCount { get; set; }

    public SpectralResidualState(int windowSize)
    {
        Window = new SlidingWindow<double>(windowSize);
        RowCount = 0;
    }
}
