namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public sealed record SsaAnomalyOptions
{
    public int WindowSize { get; init; } = 8;
    public int SeriesLength { get; init; } = 0;
    public int Rank { get; init; } = 0;
    public RankSelectionMethod RankSelection { get; init; } = RankSelectionMethod.Exact;
    public ErrorFunction ErrorFunction { get; init; } = ErrorFunction.SignedDifference;
    public AlertingMode Alerting { get; init; } = AlertingMode.PValueScore;
    public double Threshold { get; init; } = 0.01;
    public AnomalySide Side { get; init; } = AnomalySide.TwoSided;
    public MartingaleType Martingale { get; init; } = MartingaleType.None;
    public int ScoreWindowSize { get; init; } = 100;
    public bool IsChangePoint { get; init; } = false;
}

/// <summary>
/// Trains an SSA model for anomaly detection (spike or change point).
/// Fit() computes SVD of the trajectory matrix from training data.
/// </summary>
public sealed class SsaAnomalyLearner : ILearner
{
    private readonly string _inputColumn;
    private readonly SsaAnomalyOptions _options;
    private readonly ProgressSubject _progress = new();

    public SsaAnomalyLearner(
        string inputColumn = "Value",
        SsaAnomalyOptions? options = null)
    {
        _inputColumn = inputColumn;
        _options = options ?? new SsaAnomalyOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

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

    public IModel Fit(LearnerInput input)
    {
        var series = SsaTrainer.MaterializeSeries(input.TrainData, _inputColumn);
        var ssaParams = SsaTrainer.Train(
            series,
            _options.WindowSize,
            _options.SeriesLength,
            _options.Rank,
            _options.RankSelection);

        IScanTransform<SsaAnomalyState> transform = _options.IsChangePoint
            ? new SsaChangePointDetector(
                ssaParams, _inputColumn,
                _options.ErrorFunction,
                _options.Alerting,
                _options.Threshold,
                _options.Side,
                _options.Martingale,
                _options.ScoreWindowSize)
            : new SsaSpikeDetector(
                ssaParams, _inputColumn,
                _options.ErrorFunction,
                _options.Alerting,
                _options.Threshold,
                _options.Side,
                _options.Martingale,
                _options.ScoreWindowSize);

        _progress.OnCompleted();
        return new Model(new ScanTransformAdapter<SsaAnomalyState>(transform), ssaParams);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
