namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Train/test splitting and cross-validation splitting.
/// Returns views over the same underlying data — no copies.
/// </summary>
public static class DataHandleSplitter
{
    public static (IDataHandle Train, IDataHandle Test) TrainTestSplit(
        IDataHandle source,
        double testFraction = 0.2,
        int? seed = null)
    {
        var materialized = source.Materialize();
        var rowCount = (int)(materialized.RowCount
            ?? throw new InvalidOperationException("Cannot split data with unknown row count."));

        var rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var indices = Enumerable.Range(0, rowCount).ToArray();
        rng.Shuffle(indices);

        int testCount = (int)(rowCount * testFraction);
        var testIndices = indices[..testCount];
        var trainIndices = indices[testCount..];

        Array.Sort(trainIndices);
        Array.Sort(testIndices);

        return (
            new IndexedDataHandle(materialized, trainIndices),
            new IndexedDataHandle(materialized, testIndices));
    }

    public static IReadOnlyList<(IDataHandle Train, IDataHandle Test)> CrossValidationSplit(
        IDataHandle source,
        int folds = 5,
        int? seed = null)
    {
        var materialized = source.Materialize();
        var rowCount = (int)(materialized.RowCount
            ?? throw new InvalidOperationException("Cannot split data with unknown row count."));

        var rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var indices = Enumerable.Range(0, rowCount).ToArray();
        rng.Shuffle(indices);

        int foldSize = rowCount / folds;
        var results = new List<(IDataHandle, IDataHandle)>(folds);

        for (int f = 0; f < folds; f++)
        {
            int testStart = f * foldSize;
            int testEnd = (f == folds - 1) ? rowCount : testStart + foldSize;

            var testIndices = indices[testStart..testEnd];
            var trainIndices = indices[..testStart].Concat(indices[testEnd..]).ToArray();

            Array.Sort(trainIndices);
            Array.Sort(testIndices);

            results.Add((
                new IndexedDataHandle(materialized, trainIndices),
                new IndexedDataHandle(materialized, testIndices)));
        }

        return results;
    }
}

/// <summary>
/// View over a subset of rows from a materialized DataHandle, selected by index.
/// </summary>
internal sealed class IndexedDataHandle : IDataHandle
{
    private readonly IDataHandle _source;
    private readonly int[] _indices;

    public IndexedDataHandle(IDataHandle source, int[] indices)
    {
        _source = source;
        _indices = indices;
    }

    public ISchema Schema => _source.Schema;
    public long? RowCount => _indices.Length;
    public OrderingPolicy Ordering => OrderingPolicy.Ordered;
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new IndexedCursor(_source, _indices, columnsNeeded);

    public IDataHandle Materialize()
    {
        // Build a sorted order for sequential cursor scan, then scatter to output positions.
        var sortedOrder = Enumerable.Range(0, _indices.Length)
            .OrderBy(i => _indices[i])
            .ToArray();

        var columns = new Dictionary<string, Array>();
        foreach (var col in Schema.Columns)
        {
            var array = Array.CreateInstance(col.Type.ClrType, _indices.Length);
            using var cursor = _source.GetCursor([col.Name]);
            int srcRow = 0;
            int scanIdx = 0;

            while (cursor.MoveNext() && scanIdx < sortedOrder.Length)
            {
                int outputPos = sortedOrder[scanIdx];
                if (srcRow == _indices[outputPos])
                {
                    array.SetValue(cursor.Current.GetValue<object>(col.Name), outputPos);
                    scanIdx++;
                }
                srcRow++;
            }

            columns[col.Name] = array;
        }
        return new InMemoryDataHandle((Schema)_source.Schema, columns);
    }

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cursor = GetCursor(Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            yield return cursor.Current;
        }
    }

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
    {
        batch = default;
        return false;
    }
}

internal sealed class IndexedCursor : IRowCursor
{
    private readonly IDataHandle _source;
    private readonly int[] _indices;
    private readonly IEnumerable<string> _columns;
    private IRowCursor? _inner;
    private int _position = -1;
    private int _sourceRow = -1;

    public IndexedCursor(IDataHandle source, int[] indices, IEnumerable<string> columns)
    {
        _source = source;
        _indices = indices;
        _columns = columns;
        _inner = source.GetCursor(columns);
    }

    public IRow Current => _inner!.Current;

    public bool MoveNext()
    {
        _position++;
        if (_position >= _indices.Length) return false;

        int target = _indices[_position];
        while (_sourceRow < target)
        {
            if (!_inner!.MoveNext()) return false;
            _sourceRow++;
        }
        return true;
    }

    public void Dispose() => _inner?.Dispose();
}
