using System.Numerics;

namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// IScanTransform for spectral residual anomaly detection.
/// No training needed — computes FFT spectral residual on each window.
/// Window size must be a power of 2.
/// </summary>
public sealed class SpectralResidualDetector : IScanTransform<SpectralResidualState>
{
    private readonly string _inputColumn;
    private readonly int _windowSize;
    private readonly int _spectralAveragingWindow;
    private readonly int _saliencyAveragingWindow;
    private readonly double _threshold;

    public SpectralResidualDetector(
        string inputColumn = "Value",
        int windowSize = 128,
        int spectralAveragingWindow = 3,
        int saliencyAveragingWindow = 5,
        double threshold = 0.3)
    {
        // Enforce power of 2 for FFT
        _windowSize = FftHelper.NextPowerOfTwo(windowSize);
        _inputColumn = inputColumn;
        _spectralAveragingWindow = spectralAveragingWindow;
        _saliencyAveragingWindow = saliencyAveragingWindow;
        _threshold = threshold;
    }

    public TransformProperties Properties => new()
    {
        IsStateful = true,
        RequiresOrdering = true,
        PreservesRowCount = true
    };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var extra = new Schema([
            new Column("Alert", FieldType.Scalar<bool>()),
            new Column("RawScore", FieldType.Scalar<float>()),
            new Column("Magnitude", FieldType.Scalar<float>())
        ]);
        return inputSchema.MergeHorizontal(extra, ConflictPolicy.LastWriterWins);
    }

    public SpectralResidualState InitializeState() => new(_windowSize);

    public (SpectralResidualState NextState, IRow Output) ProcessRow(SpectralResidualState state, IRow input)
    {
        double observation = input.GetValue<float>(_inputColumn);
        state.Window.Push(observation);
        state.RowCount++;

        if (!state.Window.IsFull)
            return (state, MakeOutput(input, false, 0, 0));

        int n = state.Window.Count;
        var series = new double[n];
        state.Window.CopyTo(series);

        // FFT
        var complex = new Complex[n];
        for (int i = 0; i < n; i++)
            complex[i] = new Complex(series[i], 0);
        FftHelper.Transform(complex, forward: true);

        // Log amplitude spectrum
        var logAmplitude = new double[n];
        var phase = new double[n];
        for (int i = 0; i < n; i++)
        {
            logAmplitude[i] = Math.Log(complex[i].Magnitude + 1e-10);
            phase[i] = complex[i].Phase;
        }

        // Spectral residual = logAmplitude - movingAverage(logAmplitude)
        var spectralResidual = new double[n];
        for (int i = 0; i < n; i++)
        {
            double avg = 0;
            int count = 0;
            for (int j = Math.Max(0, i - _spectralAveragingWindow);
                 j <= Math.Min(n - 1, i + _spectralAveragingWindow); j++)
            {
                avg += logAmplitude[j];
                count++;
            }
            spectralResidual[i] = logAmplitude[i] - avg / count;
        }

        // IFFT of spectral residual (reconstruct with phase)
        var srComplex = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            double magnitude = Math.Exp(spectralResidual[i]);
            srComplex[i] = Complex.FromPolarCoordinates(magnitude, phase[i]);
        }
        FftHelper.Transform(srComplex, forward: false);

        // Saliency map
        var saliency = new double[n];
        for (int i = 0; i < n; i++)
            saliency[i] = srComplex[i].Magnitude * srComplex[i].Magnitude;

        // Score the last point
        double localMean = 0;
        int mCount = 0;
        int last = n - 1;
        for (int j = Math.Max(0, last - _saliencyAveragingWindow); j < last; j++)
        {
            localMean += saliency[j];
            mCount++;
        }
        localMean = mCount > 0 ? localMean / mCount : 1e-10;

        double score = Math.Abs(saliency[last] - localMean) / Math.Max(localMean, 1e-10);
        bool alert = score >= _threshold;

        return (state, MakeOutput(input, alert, (float)score, (float)saliency[last]));
    }

    public IStateSerializer<SpectralResidualState>? StateSerializer => null;

    public IDataHandle Apply(IDataHandle input)
        => new ScanDataHandle<SpectralResidualState>(this, input);

    private IRow MakeOutput(IRow input, bool alert, float score, float magnitude)
    {
        var schema = GetOutputSchema(input.Schema);
        var values = new Dictionary<string, object>();
        foreach (var col in input.Schema.Columns)
            values[col.Name] = input.GetValue<object>(col.Name);
        values["Alert"] = alert;
        values["RawScore"] = score;
        values["Magnitude"] = magnitude;
        return new DictionaryRow(schema, values);
    }
}
