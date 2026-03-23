namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Wraps any cursor-producing function as a lazy IDataHandle.
/// Used by data source adapters (CSV, Parquet, SQL) to provide
/// lazy streaming without loading everything into memory.
/// </summary>
public sealed class CursorDataHandle : IDataHandle
{
    private readonly Func<IEnumerable<string>, IRowCursor> _cursorFactory;

    public CursorDataHandle(
        ISchema schema,
        Func<IEnumerable<string>, IRowCursor> cursorFactory,
        long? rowCount = null,
        OrderingPolicy ordering = OrderingPolicy.Ordered)
    {
        Schema = schema;
        _cursorFactory = cursorFactory;
        RowCount = rowCount;
        Ordering = ordering;
    }

    public ISchema Schema { get; }
    public long? RowCount { get; }
    public OrderingPolicy Ordering { get; }
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => _cursorFactory(columnsNeeded);

    public IDataHandle Materialize()
    {
        var allColumns = Schema.Columns.Select(c => c.Name).ToArray();
        var columns = new Dictionary<string, List<object>>();
        foreach (var col in Schema.Columns)
            columns[col.Name] = [];

        using var cursor = GetCursor(allColumns);
        while (cursor.MoveNext())
        {
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

        return new InMemoryDataHandle((Schema)Schema, arrays);
    }

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var allColumns = Schema.Columns.Select(c => c.Name);
        using var cursor = GetCursor(allColumns);
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
