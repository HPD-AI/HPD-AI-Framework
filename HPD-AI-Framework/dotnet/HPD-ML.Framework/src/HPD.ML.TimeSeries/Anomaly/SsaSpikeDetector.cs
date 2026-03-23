namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// IScanTransform for SSA-based spike detection.
/// Each observation: predict → score → p-value → alert.
/// </summary>
public sealed class SsaSpikeDetector : IScanTransform<SsaAnomalyState>
{
    private readonly SsaModelParameters _params;
    private readonly string _inputColumn;
    private readonly ErrorFunction _errorFunction;
    private readonly AlertingMode _alerting;
    private readonly double _threshold;
    private readonly AnomalySide _side;
    private readonly MartingaleType _martingale;
    private readonly int _scoreWindowSize;

    public SsaSpikeDetector(
        SsaModelParameters parameters,
        string inputColumn,
        ErrorFunction errorFunction = ErrorFunction.SignedDifference,
        AlertingMode alerting = AlertingMode.PValueScore,
        double threshold = 0.01,
        AnomalySide side = AnomalySide.TwoSided,
        MartingaleType martingale = MartingaleType.None,
        int scoreWindowSize = 100)
    {
        _params = parameters;
        _inputColumn = inputColumn;
        _errorFunction = errorFunction;
        _alerting = alerting;
        _threshold = threshold;
        _side = side;
        _martingale = martingale;
        _scoreWindowSize = scoreWindowSize;
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
            new Column("PValue", FieldType.Scalar<float>()),
            new Column("MartingaleScore", FieldType.Scalar<float>())
        ]);
        return inputSchema.MergeHorizontal(extra, ConflictPolicy.LastWriterWins);
    }

    public SsaAnomalyState InitializeState()
        => new(_params, _scoreWindowSize);

    public (SsaAnomalyState NextState, IRow Output) ProcessRow(SsaAnomalyState state, IRow input)
    {
        double observation = input.GetValue<float>(_inputColumn);
        state.RowCount++;

        if (!state.ObservationWindow.IsFull)
        {
            state.ObservationWindow.Push(observation);
            return (state, MakeOutput(input, false, 0, 0.5f, 0));
        }

        // Predict next value using AR coefficients
        double predicted = PredictFromAR(state);

        // Score
        double rawScore = AnomalyScorer.ComputeRawScore(observation, predicted, _errorFunction);

        // P-value
        double pValue = AnomalyScorer.ComputeKdePValue(rawScore, state.ScoreHistory, _side);

        // Martingale
        if (_martingale != MartingaleType.None)
            state.LogMartingale = AnomalyScorer.UpdateMartingale(
                state.LogMartingale, pValue, _martingale);

        // Alert
        bool alert = AnomalyScorer.ShouldAlert(
            rawScore, pValue, state.LogMartingale, _alerting, _threshold);

        // Update state
        state.ObservationWindow.Push(observation);
        state.ScoreHistory.Push(rawScore);
        UpdateSsaState(state);

        return (state, MakeOutput(input, alert, (float)rawScore, (float)pValue,
            (float)Math.Exp(state.LogMartingale)));
    }

    public IStateSerializer<SsaAnomalyState>? StateSerializer => null;

    public IDataHandle Apply(IDataHandle input)
        => new ScanDataHandle<SsaAnomalyState>(this, input);

    internal static double PredictFromAR(SsaAnomalyState state)
    {
        double predicted = 0;
        var alpha = state.Parameters.AutoRegressiveCoefficients;
        int L = state.Parameters.WindowSize;
        for (int j = 0; j < L - 1; j++)
            predicted += alpha[j] * state.ObservationWindow[state.ObservationWindow.Count - L + 1 + j];
        return predicted;
    }

    internal static void UpdateSsaState(SsaAnomalyState state)
    {
        int rank = state.Parameters.Rank;
        int L = state.Parameters.WindowSize;
        var evecs = state.Parameters.Eigenvectors;

        for (int k = 0; k < rank; k++)
        {
            double dot = 0;
            for (int i = 0; i < L && i < state.ObservationWindow.Count; i++)
                dot += evecs[k * L + i] * state.ObservationWindow[state.ObservationWindow.Count - L + i];
            state.SsaStateVector[k] = dot;
        }
    }

    private IRow MakeOutput(IRow input, bool alert, float rawScore, float pValue, float martingale)
    {
        var schema = GetOutputSchema(input.Schema);
        var values = new Dictionary<string, object>();
        foreach (var col in input.Schema.Columns)
            values[col.Name] = input.GetValue<object>(col.Name);
        values["Alert"] = alert;
        values["RawScore"] = rawScore;
        values["PValue"] = pValue;
        values["MartingaleScore"] = martingale;
        return new DictionaryRow(schema, values);
    }
}
