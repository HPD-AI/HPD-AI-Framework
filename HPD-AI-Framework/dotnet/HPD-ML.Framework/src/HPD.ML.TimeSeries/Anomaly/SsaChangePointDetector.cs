namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// IScanTransform for SSA-based change point detection.
/// Same scoring as spike detection but with martingale-based alerting
/// and martingale reset on alert (cooldown).
/// </summary>
public sealed class SsaChangePointDetector : IScanTransform<SsaAnomalyState>
{
    private readonly SsaModelParameters _params;
    private readonly string _inputColumn;
    private readonly ErrorFunction _errorFunction;
    private readonly AlertingMode _alerting;
    private readonly double _threshold;
    private readonly AnomalySide _side;
    private readonly MartingaleType _martingale;
    private readonly int _scoreWindowSize;

    public SsaChangePointDetector(
        SsaModelParameters parameters,
        string inputColumn,
        ErrorFunction errorFunction = ErrorFunction.SignedDifference,
        AlertingMode alerting = AlertingMode.MartingaleScore,
        double threshold = 100,
        AnomalySide side = AnomalySide.TwoSided,
        MartingaleType martingale = MartingaleType.Power,
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

        double predicted = SsaSpikeDetector.PredictFromAR(state);
        double rawScore = AnomalyScorer.ComputeRawScore(observation, predicted, _errorFunction);
        double pValue = AnomalyScorer.ComputeKdePValue(rawScore, state.ScoreHistory, _side);
        state.LogMartingale = AnomalyScorer.UpdateMartingale(
            state.LogMartingale, pValue, _martingale);

        bool alert = AnomalyScorer.ShouldAlert(
            rawScore, pValue, state.LogMartingale, _alerting, _threshold);

        // Change point specific: on alert, reset martingale (cooldown)
        if (alert)
            state.LogMartingale = 0;

        state.ObservationWindow.Push(observation);
        state.ScoreHistory.Push(rawScore);
        SsaSpikeDetector.UpdateSsaState(state);

        return (state, MakeOutput(input, alert, (float)rawScore, (float)pValue,
            (float)Math.Exp(state.LogMartingale)));
    }

    public IStateSerializer<SsaAnomalyState>? StateSerializer => null;

    public IDataHandle Apply(IDataHandle input)
        => new ScanDataHandle<SsaAnomalyState>(this, input);

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
