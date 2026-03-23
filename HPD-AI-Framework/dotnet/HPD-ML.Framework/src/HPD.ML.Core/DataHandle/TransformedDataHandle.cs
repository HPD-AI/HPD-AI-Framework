namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using HPD.ML.Abstractions;

/// <summary>
/// Lazy result of applying an ITransform to an IDataHandle.
/// Does not touch data until a cursor, stream, or batch is requested.
/// </summary>
public sealed class TransformedDataHandle : IDataHandle
{
    private readonly IDataHandle _source;
    private readonly ITransform _transform;

    public TransformedDataHandle(IDataHandle source, ITransform transform)
    {
        _source = source;
        _transform = transform;
        Schema = transform.GetOutputSchema(source.Schema);
    }

    public ISchema Schema { get; }
    public long? RowCount => _transform.Properties.PreservesRowCount ? _source.RowCount : null;
    public OrderingPolicy Ordering => _source.Ordering;
    public MaterializationCapabilities Capabilities => _source.Capabilities;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
    {
        var applied = _transform.Apply(_source);
        return applied.GetCursor(columnsNeeded);
    }

    public IDataHandle Materialize()
    {
        var applied = _transform.Apply(_source);
        return applied.Materialize();
    }

    public IAsyncEnumerable<IRow> StreamRows(CancellationToken ct = default)
    {
        var applied = _transform.Apply(_source);
        return applied.StreamRows(ct);
    }

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
    {
        var applied = _transform.Apply(_source);
        return applied.TryGetColumnBatch(columnName, startRow, rowCount, out batch);
    }
}
