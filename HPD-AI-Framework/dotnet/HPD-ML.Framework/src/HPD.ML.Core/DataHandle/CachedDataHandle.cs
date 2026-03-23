namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using HPD.ML.Abstractions;

/// <summary>
/// Lazily materializes an inner IDataHandle on first access.
/// Thread-safe via Lazy&lt;T&gt;.
/// </summary>
public sealed class CachedDataHandle : IDataHandle
{
    private readonly IDataHandle _inner;
    private readonly Lazy<IDataHandle> _materialized;

    public CachedDataHandle(IDataHandle inner)
    {
        _inner = inner;
        _materialized = new Lazy<IDataHandle>(() => inner.Materialize());
    }

    public ISchema Schema => _inner.Schema;
    public long? RowCount => _materialized.IsValueCreated ? _materialized.Value.RowCount : _inner.RowCount;
    public OrderingPolicy Ordering => _inner.Ordering;

    public MaterializationCapabilities Capabilities
        => _materialized.IsValueCreated ? _materialized.Value.Capabilities : _inner.Capabilities;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => _materialized.Value.GetCursor(columnsNeeded);

    public IDataHandle Materialize() => _materialized.Value;

    public IAsyncEnumerable<IRow> StreamRows(CancellationToken ct = default)
        => _materialized.Value.StreamRows(ct);

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
        => _materialized.Value.TryGetColumnBatch(columnName, startRow, rowCount, out batch);
}
