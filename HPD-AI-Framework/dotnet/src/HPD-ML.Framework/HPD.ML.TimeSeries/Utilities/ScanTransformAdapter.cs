using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.TimeSeries;

/// <summary>
/// Adapts an IScanTransform&lt;TState&gt; into an ITransform by managing state internally.
/// The DataHandle returned by Apply() processes rows sequentially, maintaining state.
/// </summary>
public sealed class ScanTransformAdapter<TState> : ITransform
{
    private readonly IScanTransform<TState> _scan;

    public ScanTransformAdapter(IScanTransform<TState> scan) => _scan = scan;

    public TransformProperties Properties => _scan.Properties;

    public ISchema GetOutputSchema(ISchema inputSchema) => _scan.GetOutputSchema(inputSchema);

    public IDataHandle Apply(IDataHandle input)
        => new ScanDataHandle<TState>(_scan, input);
}

internal sealed class ScanDataHandle<TState> : IDataHandle
{
    private readonly IScanTransform<TState> _scan;
    private readonly IDataHandle _input;

    public ScanDataHandle(IScanTransform<TState> scan, IDataHandle input)
    {
        _scan = scan;
        _input = input;
    }

    public ISchema Schema => _scan.GetOutputSchema(_input.Schema);
    public long? RowCount => _input.RowCount;
    public OrderingPolicy Ordering => OrderingPolicy.StrictlyOrdered;
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new ScanCursor<TState>(_scan, _input.GetCursor(columnsNeeded));

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

internal sealed class ScanCursor<TState> : IRowCursor
{
    private readonly IScanTransform<TState> _scan;
    private readonly IRowCursor _inner;
    private TState _state;
    private IRow? _current;

    public ScanCursor(IScanTransform<TState> scan, IRowCursor inner)
    {
        _scan = scan;
        _inner = inner;
        _state = scan.InitializeState();
    }

    public IRow Current => _current ?? throw new InvalidOperationException("Call MoveNext first.");

    public bool MoveNext()
    {
        if (!_inner.MoveNext()) return false;
        (_state, _current) = _scan.ProcessRow(_state, _inner.Current);
        return true;
    }

    public void Dispose() => _inner.Dispose();
}
