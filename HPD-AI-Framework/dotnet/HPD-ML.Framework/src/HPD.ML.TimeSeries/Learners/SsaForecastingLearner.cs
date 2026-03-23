namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public sealed record SsaForecastOptions
{
    public int WindowSize { get; init; } = 8;
    public int SeriesLength { get; init; } = 0;
    public int Rank { get; init; } = 0;
    public RankSelectionMethod RankSelection { get; init; } = RankSelectionMethod.Exact;
    public int Horizon { get; init; } = 5;
    public float ConfidenceLevel { get; init; } = 0.95f;
}

/// <summary>
/// Trains an SSA model for forecasting.
/// Identical SVD training to SsaAnomalyLearner, but returns a SsaForecaster transform.
/// </summary>
public sealed class SsaForecastingLearner : ILearner
{
    private readonly string _inputColumn;
    private readonly SsaForecastOptions _options;
    private readonly ProgressSubject _progress = new();

    public SsaForecastingLearner(
        string inputColumn = "Value",
        SsaForecastOptions? options = null)
    {
        _inputColumn = inputColumn;
        _options = options ?? new SsaForecastOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var extra = new Schema([
            new Column("Forecast", FieldType.Vector<float>(_options.Horizon)),
            new Column("LowerBound", FieldType.Vector<float>(_options.Horizon)),
            new Column("UpperBound", FieldType.Vector<float>(_options.Horizon))
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

        var forecaster = new SsaForecaster(
            ssaParams, _inputColumn, _options.Horizon, _options.ConfidenceLevel);

        _progress.OnCompleted();
        return new Model(new ScanTransformAdapter<SsaForecastState>(forecaster), ssaParams);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
