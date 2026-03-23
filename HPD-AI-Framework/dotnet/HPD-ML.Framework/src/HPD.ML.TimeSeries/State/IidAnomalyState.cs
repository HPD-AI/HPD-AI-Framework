namespace HPD.ML.TimeSeries;

/// <summary>
/// Minimal state for IID anomaly detection. No model — just scoring history and martingale.
/// </summary>
public sealed class IidAnomalyState
{
    public SlidingWindow<double> ScoreHistory { get; }
    public double LogMartingale { get; set; }
    public long RowCount { get; set; }

    public IidAnomalyState(int scoreWindowSize)
    {
        ScoreHistory = new SlidingWindow<double>(scoreWindowSize);
        LogMartingale = 0;
        RowCount = 0;
    }
}
