namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Encodes a categorical column as indicator (one-hot) vectors.
/// Unknown values map to all-zeros.
/// </summary>
public sealed class OneHotEncodeTransform : ITransform
{
    private readonly string _columnName;
    private readonly string _outputColumnName;
    private readonly IReadOnlyDictionary<string, int> _keyMapping;
    private readonly int _categoryCount;

    public OneHotEncodeTransform(
        string columnName,
        IReadOnlyDictionary<string, int> keyMapping,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName ?? columnName;
        _keyMapping = keyMapping;
        _categoryCount = keyMapping.Count;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns
            .Where(c => c.Name != _columnName || _outputColumnName != _columnName)
            .ToList();

        var vectorCol = new Column(
            _outputColumnName,
            FieldType.Vector<float>(_categoryCount),
            AnnotationSet.Empty.With("role:KeyValues",
                _keyMapping.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray()));

        columns.Add(vectorCol);
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_columnName).Distinct()),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in outputSchema.Columns)
                    {
                        if (col.Name == _outputColumnName)
                        {
                            var raw = row.GetValue<object>(_columnName)?.ToString() ?? "";
                            var vector = new float[_categoryCount];
                            if (_keyMapping.TryGetValue(raw, out int idx))
                                vector[idx] = 1f;
                            values[_outputColumnName] = vector;
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
/// Fits a OneHotEncodeTransform by scanning for unique values.
/// </summary>
public sealed class OneHotEncodeLearner : ILearner
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly int _maxCategories;

    public OneHotEncodeLearner(
        string columnName,
        int maxCategories = 1000,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _maxCategories = maxCategories;
        _outputColumnName = outputColumnName;
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new OneHotEncodeTransform(_columnName, new Dictionary<string, int>(), _outputColumnName)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        var mapping = new Dictionary<string, int>();
        using var cursor = input.TrainData.GetCursor([_columnName]);
        while (cursor.MoveNext())
        {
            var val = cursor.Current.GetValue<object>(_columnName)?.ToString() ?? "";
            if (!mapping.ContainsKey(val) && mapping.Count < _maxCategories)
                mapping[val] = mapping.Count;
        }

        var transform = new OneHotEncodeTransform(_columnName, mapping, _outputColumnName);
        return new Model(transform, new OneHotParameters(mapping));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));
}

public sealed record OneHotParameters(
    IReadOnlyDictionary<string, int> KeyMapping) : ILearnedParameters;
