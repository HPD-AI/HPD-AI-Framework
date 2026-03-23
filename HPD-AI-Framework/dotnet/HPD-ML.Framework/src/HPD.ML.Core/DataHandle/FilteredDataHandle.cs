namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Lazy row filtering. Does not touch data until cursored.
/// </summary>
public sealed class FilteredDataHandle : IDataHandle
{
    private readonly IDataHandle _source;
    private readonly Func<IRow, bool> _predicate;

    public FilteredDataHandle(IDataHandle source, Func<IRow, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
    }

    public ISchema Schema => _source.Schema;
    public long? RowCount => null;
    public OrderingPolicy Ordering => _source.Ordering;
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new FilteredCursor(_source.GetCursor(columnsNeeded), _predicate);

    public IDataHandle Materialize()
    {
        var columns = new Dictionary<string, List<object>>();
        foreach (var col in Schema.Columns)
            columns[col.Name] = [];

        using var cursor = _source.GetCursor(Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext())
        {
            if (!_predicate(cursor.Current)) continue;
            foreach (var col in Schema.Columns)
                columns[col.Name].Add(cursor.Current.GetValue<object>(col.Name));
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

        return new InMemoryDataHandle((Schema)_source.Schema, arrays);
    }

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var row in _source.StreamRows(ct))
        {
            if (_predicate(row))
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

internal sealed class FilteredCursor : IRowCursor
{
    private readonly IRowCursor _inner;
    private readonly Func<IRow, bool> _predicate;

    public FilteredCursor(IRowCursor inner, Func<IRow, bool> predicate)
    {
        _inner = inner;
        _predicate = predicate;
    }

    public IRow Current => _inner.Current;

    public bool MoveNext()
    {
        while (_inner.MoveNext())
        {
            if (_predicate(_inner.Current))
                return true;
        }
        return false;
    }

    public void Dispose() => _inner.Dispose();
}
