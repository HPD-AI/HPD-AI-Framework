namespace HPD.ML.TimeSeries;

/// <summary>
/// Inference state for SSA-based anomaly detection.
/// </summary>
public sealed class SsaAnomalyState
{
    public SlidingWindow<double> ObservationWindow { get; }
    public SlidingWindow<double> ScoreHistory { get; }
    public double[] SsaStateVector { get; set; }
    public double LogMartingale { get; set; }
    public long RowCount { get; set; }
    public SsaModelParameters Parameters { get; set; }

    public SsaAnomalyState(SsaModelParameters parameters, int scoreWindowSize)
    {
        ObservationWindow = new SlidingWindow<double>(parameters.WindowSize);
        ScoreHistory = new SlidingWindow<double>(scoreWindowSize);
        SsaStateVector = (double[])parameters.InitialStateVector.Clone();
        LogMartingale = 0;
        RowCount = 0;
        Parameters = parameters;
    }
}
