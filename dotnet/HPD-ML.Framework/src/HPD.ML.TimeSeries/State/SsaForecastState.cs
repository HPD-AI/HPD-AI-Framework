namespace HPD.ML.TimeSeries;

/// <summary>
/// Inference state for SSA forecasting.
/// </summary>
public sealed class SsaForecastState
{
    public SlidingWindow<double> ObservationWindow { get; }
    public double[] SsaStateVector { get; set; }
    public long RowCount { get; set; }
    public SsaModelParameters Parameters { get; set; }

    public SsaForecastState(SsaModelParameters parameters)
    {
        ObservationWindow = new SlidingWindow<double>(parameters.WindowSize);
        SsaStateVector = (double[])parameters.InitialStateVector.Clone();
        RowCount = 0;
        Parameters = parameters;
    }
}
