namespace HPD.ML.Evaluation;

using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes permutation feature importance by shuffling each feature column
/// and measuring the impact on a metric.
/// </summary>
public static class PermutationFeatureImportance
{
    public static IDataHandle Compute(
        IModel model,
        IDataHandle data,
        ITransform metricsTransform,
        string metricName,
        IReadOnlyList<string> featureColumns,
        int permutations = 5,
        int? seed = null)
    {
        // Baseline metric
        var baselinePredictions = model.Transform.Apply(data);
        var baselineMetrics = metricsTransform.Apply(baselinePredictions);
        double baselineScore = MetricHelpers.ReadMetric(baselineMetrics, metricName);

        var featureNames = new List<string>();
        var metricDrops = new List<double>();
        var metricDropStdDevs = new List<double>();

        // Materialize data once for efficient column access
        var materialized = data.Materialize();

        foreach (var featureCol in featureColumns)
        {
            var drops = new List<double>();

            for (int p = 0; p < permutations; p++)
            {
                int permSeed = seed.HasValue ? seed.Value + p : Random.Shared.Next();

                var shuffled = new ShuffledColumnDataHandle(materialized, featureCol, permSeed);
                var predictions = model.Transform.Apply(shuffled);
                var metrics = metricsTransform.Apply(predictions);
                double score = MetricHelpers.ReadMetric(metrics, metricName);
                drops.Add(baselineScore - score);
            }

            featureNames.Add(featureCol);
            double mean = drops.Average();
            metricDrops.Add(mean);
            metricDropStdDevs.Add(drops.Count > 1
                ? Math.Sqrt(drops.Average(d => (d - mean) * (d - mean)))
                : 0);
        }

        return InMemoryDataHandle.FromColumns(
            ("FeatureName", featureNames.ToArray()),
            ("MetricDrop", metricDrops.ToArray()),
            ("MetricDropStdDev", metricDropStdDevs.ToArray()));
    }
}

/// <summary>
/// A DataHandle wrapper that lazily shuffles a single column via cursor remapping.
/// Avoids materializing and copying the entire dataset.
/// </summary>
internal sealed class ShuffledColumnDataHandle : IDataHandle
{
    private readonly IDataHandle _source;
    private readonly string _shuffleColumn;
    private readonly int[] _permutation;

    public ShuffledColumnDataHandle(IDataHandle source, string shuffleColumn, int seed)
    {
        _source = source;
        _shuffleColumn = shuffleColumn;

        // Build shuffle permutation
        int n = (int)(source.RowCount ?? 0);
        _permutation = Enumerable.Range(0, n).ToArray();
        var rng = new Random(seed);
        rng.Shuffle(_permutation);
    }

    public ISchema Schema => _source.Schema;
    public long? RowCount => _source.RowCount;
    public OrderingPolicy Ordering => _source.Ordering;
    public MaterializationCapabilities Capabilities => _source.Capabilities;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
    {
        var cols = columnsNeeded.ToArray();
        bool needsShuffle = cols.Contains(_shuffleColumn);
        if (!needsShuffle)
            return _source.GetCursor(cols);

        return new ShuffledCursor(_source, cols, _shuffleColumn, _permutation);
    }

    public IDataHandle Materialize() => this;

    public async IAsyncEnumerable<IRow> StreamRows(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cursor = GetCursor(Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            yield return cursor.Current;
        }
    }

    public bool TryGetColumnBatch<T>(string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, System.Numerics.INumber<T>
    {
        batch = default;
        return false;
    }
}

internal sealed class ShuffledCursor : IRowCursor
{
    private readonly IRowCursor _sourceCursor;
    private readonly string _shuffleColumn;
    private int _position = -1;

    public ShuffledCursor(IDataHandle source, string[] columns, string shuffleColumn, int[] permutation)
    {
        _shuffleColumn = shuffleColumn;
        _sourceCursor = source.GetCursor(columns);
        _shuffledValues = ReadShuffledValues(source, shuffleColumn, permutation);
    }

    private readonly object[] _shuffledValues;

    private static object[] ReadShuffledValues(IDataHandle source, string column, int[] permutation)
    {
        var values = new object[permutation.Length];
        using var cursor = source.GetCursor([column]);
        int idx = 0;
        while (cursor.MoveNext() && idx < values.Length)
        {
            values[idx] = cursor.Current.GetValue<object>(column);
            idx++;
        }

        // Apply permutation
        var result = new object[values.Length];
        for (int i = 0; i < permutation.Length; i++)
            result[i] = values[permutation[i]];
        return result;
    }

    public IRow Current => new ShuffledRow(_sourceCursor.Current, _shuffleColumn, _shuffledValues, _position);

    public bool MoveNext()
    {
        _position++;
        return _sourceCursor.MoveNext();
    }

    public void Dispose()
    {
        _sourceCursor.Dispose();
    }
}

internal sealed class ShuffledRow : IRow
{
    private readonly IRow _inner;
    private readonly string _shuffleColumn;
    private readonly object[] _shuffledValues;
    private readonly int _position;

    public ShuffledRow(IRow inner, string shuffleColumn, object[] shuffledValues, int position)
    {
        _inner = inner;
        _shuffleColumn = shuffleColumn;
        _shuffledValues = shuffledValues;
        _position = position;
    }

    public ISchema Schema => _inner.Schema;

    public T GetValue<T>(string columnName) where T : allows ref struct
    {
        if (columnName == _shuffleColumn && _position < _shuffledValues.Length)
        {
            var val = _shuffledValues[_position];
            if (typeof(T) == typeof(object))
                return Unsafe.As<object, T>(ref val);
            if (!typeof(T).IsValueType && val is T)
                return Unsafe.As<object, T>(ref val);
            // Delegate to inner row's coercion for value types
            return _inner.GetValue<T>(columnName);
        }
        return _inner.GetValue<T>(columnName);
    }

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
    {
        if (columnName == _shuffleColumn && _position < _shuffledValues.Length)
        {
            var val = _shuffledValues[_position];
            if (typeof(T) == typeof(object))
            {
                value = Unsafe.As<object, T>(ref val);
                return true;
            }
            if (!typeof(T).IsValueType && val is T)
            {
                value = Unsafe.As<object, T>(ref val);
                return true;
            }
            // For value types, create a DictionaryRow for coercion
            var dict = new Dictionary<string, object> { [columnName] = val };
            var tempRow = new DictionaryRow(_inner.Schema, dict);
            return tempRow.TryGetValue(columnName, out value);
        }
        return _inner.TryGetValue(columnName, out value);
    }
}
