namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Assigns values to bins using equal-density (quantile) binning.
/// Output is the bin index normalized to [0, 1].
/// </summary>
public sealed class BinNormalizeTransform : ITransform
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly float[] _binEdges;

    public BinNormalizeTransform(
        string columnName,
        float[] binEdges,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _binEdges = binEdges;
        _outputColumnName = outputColumnName;
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
        int numBins = _binEdges.Length + 1;

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
                            float val = row.GetValue<float>(_columnName);
                            int bin = Array.BinarySearch(_binEdges, val);
                            if (bin < 0) bin = ~bin;
                            values[outCol] = numBins > 1 ? (float)bin / (numBins - 1) : 0f;
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
/// Fits a BinNormalizeTransform by computing quantile boundaries.
/// </summary>
public sealed class BinNormalizeLearner : ILearner
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly int _numBins;

    public BinNormalizeLearner(string columnName, int numBins = 10, string? outputColumnName = null)
    {
        _columnName = columnName;
        _numBins = numBins;
        _outputColumnName = outputColumnName;
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new BinNormalizeTransform(_columnName, [], _outputColumnName)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        var allValues = new List<float>();
        using var cursor = input.TrainData.GetCursor([_columnName]);
        while (cursor.MoveNext())
            allValues.Add(cursor.Current.GetValue<float>(_columnName));

        allValues.Sort();

        var edges = new float[_numBins - 1];
        for (int i = 0; i < edges.Length; i++)
        {
            int idx = (int)((i + 1.0) / _numBins * allValues.Count);
            idx = Math.Min(idx, allValues.Count - 1);
            edges[i] = allValues[idx];
        }

        var transform = new BinNormalizeTransform(_columnName, edges, _outputColumnName);
        return new Model(transform, new BinParameters(edges));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));
}

public sealed record BinParameters(float[] Edges) : ILearnedParameters;
