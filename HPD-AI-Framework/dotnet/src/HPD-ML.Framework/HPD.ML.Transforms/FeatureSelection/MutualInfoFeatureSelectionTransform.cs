namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Selects top-K features by mutual information with a label column.
/// Mutual information measures the dependency between each feature and the label:
/// MI(X,Y) = Σ p(x,y) * log(p(x,y) / (p(x) * p(y)))
/// Discretizes continuous features into bins before computing MI.
/// </summary>
public sealed class MutualInfoFeatureSelectionTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly IReadOnlyList<string> _selectedColumns;

    public MutualInfoFeatureSelectionTransform(
        string labelColumn,
        IReadOnlyList<string> selectedColumns)
    {
        _labelColumn = labelColumn;
        _selectedColumns = selectedColumns;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var keep = new HashSet<string>(_selectedColumns) { _labelColumn };
        var columns = inputSchema.Columns.Where(c => keep.Contains(c.Name)).ToList();
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var keepSet = new HashSet<string>(_selectedColumns) { _labelColumn };

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Union(keepSet)),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in outputSchema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);
                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }
}

/// <summary>
/// Fits a MutualInfoFeatureSelectionTransform by computing mutual information
/// between each feature column and the label, then selecting top-K.
/// </summary>
public sealed class MutualInfoFeatureSelectionLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string[] _featureColumns;
    private readonly int _topK;
    private readonly int _numBins;

    public MutualInfoFeatureSelectionLearner(
        string labelColumn,
        string[] featureColumns,
        int topK = 10,
        int numBins = 32)
    {
        _labelColumn = labelColumn;
        _featureColumns = featureColumns;
        _topK = topK;
        _numBins = numBins;
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new MutualInfoFeatureSelectionTransform(_labelColumn, _featureColumns.Take(_topK).ToList())
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        // Collect all feature + label values
        var featureValues = _featureColumns.ToDictionary(c => c, _ => new List<float>());
        var labelValues = new List<float>();

        using var cursor = input.TrainData.GetCursor([.. _featureColumns, _labelColumn]);
        while (cursor.MoveNext())
        {
            foreach (var col in _featureColumns)
                featureValues[col].Add(ToFloat(cursor.Current.GetValue<object>(col)));
            labelValues.Add(ToFloat(cursor.Current.GetValue<object>(_labelColumn)));
        }

        int n = labelValues.Count;
        if (n == 0)
        {
            var transform = new MutualInfoFeatureSelectionTransform(_labelColumn, Array.Empty<string>());
            return new Model(transform, new MutualInfoParameters(new Dictionary<string, double>()));
        }

        // Discretize label
        var labelBins = Discretize(labelValues, _numBins);

        // Compute MI for each feature
        var scores = new Dictionary<string, double>();
        foreach (var col in _featureColumns)
        {
            var featureBins = Discretize(featureValues[col], _numBins);
            scores[col] = ComputeMutualInformation(featureBins, labelBins, n);
        }

        // Select top-K
        var selected = scores
            .OrderByDescending(kv => kv.Value)
            .Take(_topK)
            .Select(kv => kv.Key)
            .ToList();

        var selectedScores = scores
            .Where(kv => selected.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var result = new MutualInfoFeatureSelectionTransform(_labelColumn, selected);
        return new Model(result, new MutualInfoParameters(selectedScores));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));

    private static float ToFloat(object? val) => val switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        long l => l,
        _ => 0f
    };

    internal static int[] Discretize(List<float> values, int numBins)
    {
        var sorted = values.ToList();
        sorted.Sort();

        var edges = new float[numBins - 1];
        for (int i = 0; i < edges.Length; i++)
        {
            int idx = (int)((i + 1.0) / numBins * sorted.Count);
            idx = Math.Min(idx, sorted.Count - 1);
            edges[i] = sorted[idx];
        }

        var bins = new int[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            int bin = Array.BinarySearch(edges, values[i]);
            bins[i] = bin < 0 ? ~bin : bin;
        }
        return bins;
    }

    internal static double ComputeMutualInformation(int[] x, int[] y, int n)
    {
        // Joint and marginal counts
        var joint = new Dictionary<(int, int), int>();
        var xCounts = new Dictionary<int, int>();
        var yCounts = new Dictionary<int, int>();

        for (int i = 0; i < n; i++)
        {
            var key = (x[i], y[i]);
            joint[key] = joint.GetValueOrDefault(key) + 1;
            xCounts[x[i]] = xCounts.GetValueOrDefault(x[i]) + 1;
            yCounts[y[i]] = yCounts.GetValueOrDefault(y[i]) + 1;
        }

        double mi = 0;
        foreach (var ((xi, yi), count) in joint)
        {
            double pxy = (double)count / n;
            double px = (double)xCounts[xi] / n;
            double py = (double)yCounts[yi] / n;
            if (pxy > 0 && px > 0 && py > 0)
                mi += pxy * Math.Log(pxy / (px * py));
        }

        return mi;
    }
}

public sealed record MutualInfoParameters(
    IReadOnlyDictionary<string, double> FeatureScores) : ILearnedParameters;
