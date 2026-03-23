namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Normalizes to zero mean and unit variance (z-score normalization).
/// Formula: (value - mean) / stddev
/// </summary>
public sealed class MeanVarianceNormalizeTransform : ITransform
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly double _mean;
    private readonly double _stdDev;

    public MeanVarianceNormalizeTransform(
        string columnName,
        double mean,
        double stdDev,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName;
        _mean = mean;
        _stdDev = stdDev == 0 ? 1.0 : stdDev;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        if (_outputColumnName is null || _outputColumnName == _columnName)
            return inputSchema;

        var newCol = new Column(_outputColumnName, FieldType.Scalar<float>());
        return new Schema(inputSchema.Columns.Append(newCol).ToList(), inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var outCol = _outputColumnName ?? _columnName;

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_columnName).Distinct()),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in outputSchema.Columns)
                    {
                        if (col.Name == outCol)
                        {
                            double raw = row.GetValue<float>(_columnName);
                            double normalized = (raw - _mean) / _stdDev;
                            values[outCol] = (float)normalized;
                        }
                        else
                        {
                            values[col.Name] = row.GetValue<object>(col.Name);
                        }
                    }
                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }
}

/// <summary>
/// Fits a MeanVarianceNormalizeTransform by computing column statistics.
/// </summary>
public sealed class MeanVarianceNormalizeLearner : ILearner
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;

    public MeanVarianceNormalizeLearner(
        string columnName,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName;
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new MeanVarianceNormalizeTransform(_columnName, 0, 1, _outputColumnName)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        double sum = 0, sumSq = 0;
        long count = 0;

        using var cursor = input.TrainData.GetCursor([_columnName]);
        while (cursor.MoveNext())
        {
            double val = cursor.Current.GetValue<float>(_columnName);
            sum += val;
            sumSq += val * val;
            count++;
        }

        double mean = count > 0 ? sum / count : 0;
        double variance = count > 0 ? (sumSq / count) - (mean * mean) : 0;
        double stdDev = Math.Sqrt(variance);

        var transform = new MeanVarianceNormalizeTransform(
            _columnName, mean, stdDev, _outputColumnName);

        return new Model(transform, new MeanVarianceParameters(mean, stdDev));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));
}

public sealed record MeanVarianceParameters(double Mean, double StdDev) : ILearnedParameters;
