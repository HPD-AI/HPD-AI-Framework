namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// IScanTransform for IID spike detection.
/// No model — the input value IS the raw anomaly score.
/// Applies KDE p-value and optional martingale scoring.
/// </summary>
public sealed class IidSpikeDetector : IScanTransform<IidAnomalyState>
{
    private readonly string _inputColumn;
    private readonly AlertingMode _alerting;
    private readonly double _threshold;
    private readonly AnomalySide _side;
    private readonly MartingaleType _martingale;
    private readonly int _scoreWindowSize;

    public IidSpikeDetector(
        string inputColumn = "Value",
        AlertingMode alerting = AlertingMode.PValueScore,
        double threshold = 0.01,
        AnomalySide side = AnomalySide.TwoSided,
        MartingaleType martingale = MartingaleType.None,
        int scoreWindowSize = 100)
    {
        _inputColumn = inputColumn;
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
            new Column("PValue", FieldType.Scalar<float>())
        ]);
        return inputSchema.MergeHorizontal(extra, ConflictPolicy.LastWriterWins);
    }

    public IidAnomalyState InitializeState() => new(_scoreWindowSize);

    public (IidAnomalyState NextState, IRow Output) ProcessRow(IidAnomalyState state, IRow input)
    {
        double rawScore = input.GetValue<float>(_inputColumn);
        state.RowCount++;

        double pValue = AnomalyScorer.ComputeKdePValue(rawScore, state.ScoreHistory, _side);

        if (_martingale != MartingaleType.None)
            state.LogMartingale = AnomalyScorer.UpdateMartingale(
                state.LogMartingale, pValue, _martingale);

        bool alert = AnomalyScorer.ShouldAlert(rawScore, pValue, state.LogMartingale, _alerting, _threshold);

        state.ScoreHistory.Push(rawScore);

        var schema = GetOutputSchema(input.Schema);
        var values = new Dictionary<string, object>();
        foreach (var col in input.Schema.Columns)
            values[col.Name] = input.GetValue<object>(col.Name);
        values["Alert"] = alert;
        values["RawScore"] = (float)rawScore;
        values["PValue"] = (float)pValue;
        return (state, new DictionaryRow(schema, values));
    }

    public IStateSerializer<IidAnomalyState>? StateSerializer => null;

    public IDataHandle Apply(IDataHandle input)
        => new ScanDataHandle<IidAnomalyState>(this, input);
}
