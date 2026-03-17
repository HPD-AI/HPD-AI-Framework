namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Vertical concatenation — appends rows from multiple DataHandles.
/// Schemas must be vertically compatible (same columns, same types).
/// </summary>
public sealed class ConcatenatedDataHandle : IDataHandle
{
    private readonly IDataHandle[] _sources;

    public ConcatenatedDataHandle(params ReadOnlySpan<IDataHandle> sources)
    {
        if (sources.Length == 0) throw new ArgumentException("At least one source required.");

        var first = sources[0];
        for (int i = 1; i < sources.Length; i++)
            first.Schema.MergeVertical(sources[i].Schema); // throws on mismatch

        _sources = sources.ToArray();
    }

    public ISchema Schema => _sources[0].Schema;

    public long? RowCount
    {
        get
        {
            long total = 0;
            foreach (var s in _sources)
            {
                if (s.RowCount is null) return null;
                total += s.RowCount.Value;
            }
            return total;
        }
    }

    public OrderingPolicy Ordering => OrderingPolicy.Ordered;
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new ConcatenatedCursor(_sources, columnsNeeded);

    public IDataHandle Materialize()
    {
        var columns = new Dictionary<string, List<object>>();
        foreach (var col in Schema.Columns)
            columns[col.Name] = [];

        foreach (var source in _sources)
        {
            using var cursor = source.GetCursor(Schema.Columns.Select(c => c.Name));
            while (cursor.MoveNext())
            {
                foreach (var col in Schema.Columns)
                    columns[col.Name].Add(cursor.Current.GetValue<object>(col.Name));
            }
        }

        var arrays = new Dictionary<string, Array>();
        foreach (var col in Schema.Columns)
        {
            var list = columns[col.Name];
            var array = Array.CreateInstance(col.Type.ClrType, list.Count);
            for (int i = 0; i < list.Count; i++)
                array.SetValue(list[i], i);
            arrays[col.Name] = array;
        }

        return new InMemoryDataHandle((Schema)Schema, arrays);
    }

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var source in _sources)
        {
            await foreach (var row in source.StreamRows(ct))
                yield return row;
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

internal sealed class ConcatenatedCursor : IRowCursor
{
    private readonly IDataHandle[] _sources;
    private readonly IEnumerable<string> _columns;
    private int _sourceIndex;
    private IRowCursor? _current;

    public ConcatenatedCursor(IDataHandle[] sources, IEnumerable<string> columns)
    {
        _sources = sources;
        _columns = columns;
        _sourceIndex = 0;
        _current = sources[0].GetCursor(columns);
    }

    public IRow Current => _current!.Current;

    public bool MoveNext()
    {
        while (_current is not null)
        {
            if (_current.MoveNext()) return true;
            _current.Dispose();
            _sourceIndex++;
            _current = _sourceIndex < _sources.Length
                ? _sources[_sourceIndex].GetCursor(_columns)
                : null;
        }
        return false;
    }

    public void Dispose() => _current?.Dispose();
}
