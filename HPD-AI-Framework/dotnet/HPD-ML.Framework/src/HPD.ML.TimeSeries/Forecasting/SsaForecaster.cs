namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// IScanTransform for SSA-based multi-step forecasting.
/// Each observation: consume → update state → forecast h steps ahead.
/// </summary>
public sealed class SsaForecaster : IScanTransform<SsaForecastState>
{
    private readonly SsaModelParameters _params;
    private readonly string _inputColumn;
    private readonly int _horizon;
    private readonly float _confidenceLevel;

    public SsaForecaster(
        SsaModelParameters parameters,
        string inputColumn = "Value",
        int horizon = 5,
        float confidenceLevel = 0.95f)
    {
        _params = parameters;
        _inputColumn = inputColumn;
        _horizon = horizon;
        _confidenceLevel = confidenceLevel;
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
            new Column("Forecast", FieldType.Vector<float>(_horizon)),
            new Column("LowerBound", FieldType.Vector<float>(_horizon)),
            new Column("UpperBound", FieldType.Vector<float>(_horizon))
        ]);
        return inputSchema.MergeHorizontal(extra, ConflictPolicy.LastWriterWins);
    }

    public SsaForecastState InitializeState() => new(_params);

    public (SsaForecastState NextState, IRow Output) ProcessRow(SsaForecastState state, IRow input)
    {
        double observation = input.GetValue<float>(_inputColumn);
        state.ObservationWindow.Push(observation);
        state.RowCount++;

        if (!state.ObservationWindow.IsFull)
        {
            var empty = new float[_horizon];
            return (state, MakeOutput(input, empty, empty, empty));
        }

        // Forecast h steps using AR coefficients
        var alpha = state.Parameters.AutoRegressiveCoefficients;
        int L = state.Parameters.WindowSize;

        var extended = new double[L + _horizon];
        for (int i = 0; i < L; i++)
            extended[i] = state.ObservationWindow[state.ObservationWindow.Count - L + i];

        for (int h = 0; h < _horizon; h++)
        {
            double predicted = 0;
            for (int j = 0; j < L - 1; j++)
                predicted += alpha[j] * extended[L + h - (L - 1) + j];
            extended[L + h] = predicted;
        }

        var forecast = new float[_horizon];
        for (int h = 0; h < _horizon; h++)
            forecast[h] = (float)extended[L + h];

        // Confidence intervals
        double residualVariance = ComputeResidualVariance(state, alpha, L);
        double zScore = ConfidenceToZ(_confidenceLevel);

        var lower = new float[_horizon];
        var upper = new float[_horizon];
        for (int h = 0; h < _horizon; h++)
        {
            double margin = zScore * Math.Sqrt(residualVariance * (h + 1));
            lower[h] = forecast[h] - (float)margin;
            upper[h] = forecast[h] + (float)margin;
        }

        // Update SSA state vector
        int rank = state.Parameters.Rank;
        var evecs = state.Parameters.Eigenvectors;
        for (int k = 0; k < rank; k++)
        {
            double dot = 0;
            for (int i = 0; i < L && i < state.ObservationWindow.Count; i++)
                dot += evecs[k * L + i] * state.ObservationWindow[state.ObservationWindow.Count - L + i];
            state.SsaStateVector[k] = dot;
        }

        return (state, MakeOutput(input, forecast, lower, upper));
    }

    public IStateSerializer<SsaForecastState>? StateSerializer => null;

    public IDataHandle Apply(IDataHandle input)
        => new ScanDataHandle<SsaForecastState>(this, input);

    private static double ComputeResidualVariance(SsaForecastState state, double[] alpha, int L)
    {
        if (state.ObservationWindow.Count < L + 1)
            return 1.0;

        double sumSq = 0;
        int count = 0;
        for (int t = L; t < state.ObservationWindow.Count; t++)
        {
            double predicted = 0;
            for (int j = 0; j < L - 1; j++)
                predicted += alpha[j] * state.ObservationWindow[t - L + 1 + j];
            double residual = state.ObservationWindow[t] - predicted;
            sumSq += residual * residual;
            count++;
        }

        return count > 0 ? sumSq / count : 1.0;
    }

    private static double ConfidenceToZ(float level) => level switch
    {
        >= 0.99f => 2.576,
        >= 0.95f => 1.96,
        >= 0.90f => 1.645,
        >= 0.80f => 1.282,
        _ => 1.96
    };

    private IRow MakeOutput(IRow input, float[] forecast, float[] lower, float[] upper)
    {
        var schema = GetOutputSchema(input.Schema);
        var values = new Dictionary<string, object>();
        foreach (var col in input.Schema.Columns)
            values[col.Name] = input.GetValue<object>(col.Name);
        values["Forecast"] = forecast;
        values["LowerBound"] = lower;
        values["UpperBound"] = upper;
        return new DictionaryRow(schema, values);
    }
}
