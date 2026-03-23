namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;

/// <summary>
/// Columnar in-memory data. Supports cursor, async stream, and zero-copy tensor batch access.
/// This is what Materialize() returns.
/// </summary>
public sealed class InMemoryDataHandle : IDataHandle
{
    private readonly Dictionary<string, Array> _columns;
    private readonly long _rowCount;

    public InMemoryDataHandle(Schema schema, Dictionary<string, Array> columns)
    {
        Schema = schema;
        _columns = columns;
        _rowCount = columns.Values.FirstOrDefault()?.LongLength ?? 0;
    }

    public ISchema Schema { get; }
    public long? RowCount => _rowCount;
    public OrderingPolicy Ordering => OrderingPolicy.StrictlyOrdered;

    public MaterializationCapabilities Capabilities
        => MaterializationCapabilities.ColumnarAccess
         | MaterializationCapabilities.BatchAccess
         | MaterializationCapabilities.KnownDensity;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new ArrayRowCursor(Schema, _columns, columnsNeeded.ToArray(), _rowCount);

    public IDataHandle Materialize() => this;

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (long i = 0; i < _rowCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return new Row(Schema, _columns, i);
        }
    }

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
    {
        if (_columns.TryGetValue(columnName, out var array) && array is T[] typed)
        {
            var slice = typed.AsSpan(startRow, Math.Min(rowCount, typed.Length - startRow));
            batch = new ReadOnlyTensorSpan<T>(slice, [slice.Length]);
            return true;
        }
        batch = default;
        return false;
    }

    /// <summary>Build from column arrays. Schema is inferred from column types.</summary>
    public static InMemoryDataHandle FromColumns(params ReadOnlySpan<(string Name, Array Values)> columns)
    {
        var builder = new SchemaBuilder();
        var dict = new Dictionary<string, Array>(columns.Length);
        foreach (var (name, values) in columns)
        {
            var elementType = values.GetType().GetElementType()!;
            builder.AddColumn(name, new FieldType(elementType));
            dict[name] = values;
        }
        return new InMemoryDataHandle(builder.Build(), dict);
    }

    /// <summary>Build from IEnumerable&lt;T&gt; via source-generated schema.</summary>
    public static InMemoryDataHandle FromEnumerable<T>(
        IEnumerable<T> items,
        Func<T, Dictionary<string, object>> rowExtractor,
        Schema schema)
    {
        var columns = new Dictionary<string, List<object>>();
        foreach (var col in schema.Columns)
            columns[col.Name] = [];

        foreach (var item in items)
        {
            var row = rowExtractor(item);
            foreach (var (name, value) in row)
                columns[name].Add(value);
        }

        var arrays = new Dictionary<string, Array>();
        foreach (var col in schema.Columns)
        {
            var list = columns[col.Name];
            var array = Array.CreateInstance(col.Type.ClrType, list.Count);
            for (int i = 0; i < list.Count; i++)
                array.SetValue(list[i], i);
            arrays[col.Name] = array;
        }

        return new InMemoryDataHandle(schema, arrays);
    }
}
