namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Scales column values to [scaleMin, scaleMax] range.
/// Formula: (value - dataMin) / (dataMax - dataMin) * (scaleMax - scaleMin) + scaleMin
/// </summary>
public sealed class MinMaxNormalizeTransform : ITransform
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly float _dataMin;
    private readonly float _dataMax;
    private readonly float _scaleMin;
    private readonly float _scaleMax;

    public MinMaxNormalizeTransform(
        string columnName,
        float dataMin,
        float dataMax,
        float scaleMin = 0f,
        float scaleMax = 1f,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName;
        _dataMin = dataMin;
        _dataMax = dataMax;
        _scaleMin = scaleMin;
        _scaleMax = scaleMax;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        _ = inputSchema.FindByName(_columnName)
            ?? throw new InvalidOperationException($"Column '{_columnName}' not found.");

        if (_outputColumnName is null || _outputColumnName == _columnName)
            return inputSchema;

        var newCol = new Column(_outputColumnName, FieldType.Scalar<float>());
        return new Schema(inputSchema.Columns.Append(newCol).ToList(), inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var outCol = _outputColumnName ?? _columnName;
        float range = _dataMax - _dataMin;
        float scale = (_scaleMax - _scaleMin) / (range == 0 ? 1f : range);

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
                            float raw = row.GetValue<float>(_columnName);
                            float normalized = (raw - _dataMin) * scale + _scaleMin;
                            values[outCol] = normalized;
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
/// Fits a MinMaxNormalizeTransform by scanning data for min/max.
/// </summary>
public sealed class MinMaxNormalizeLearner : ILearner
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly float _scaleMin;
    private readonly float _scaleMax;

    public MinMaxNormalizeLearner(
        string columnName,
        float scaleMin = 0f,
        float scaleMax = 1f,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _scaleMin = scaleMin;
        _scaleMax = scaleMax;
        _outputColumnName = outputColumnName;
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new MinMaxNormalizeTransform(_columnName, 0, 1, _scaleMin, _scaleMax, _outputColumnName)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        float min = float.MaxValue, max = float.MinValue;

        using var cursor = input.TrainData.GetCursor([_columnName]);
        while (cursor.MoveNext())
        {
            float val = cursor.Current.GetValue<float>(_columnName);
            if (val < min) min = val;
            if (val > max) max = val;
        }

        var transform = new MinMaxNormalizeTransform(
            _columnName, min, max, _scaleMin, _scaleMax, _outputColumnName);

        return new Model(transform, new NormalizationParameters(min, max));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));
}

public sealed record NormalizationParameters(float Min, float Max) : ILearnedParameters;
