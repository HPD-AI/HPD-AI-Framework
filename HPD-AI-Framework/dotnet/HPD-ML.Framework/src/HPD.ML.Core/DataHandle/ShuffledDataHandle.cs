namespace HPD.ML.Core;

using System.Numerics;
using System.Numerics.Tensors;
using HPD.ML.Abstractions;

/// <summary>
/// Randomized row ordering. Materializes on first access (must know all rows to shuffle).
/// </summary>
public sealed class ShuffledDataHandle : IDataHandle
{
    private readonly IDataHandle _source;
    private readonly int? _seed;
    private readonly Lazy<IDataHandle> _shuffled;

    public ShuffledDataHandle(IDataHandle source, int? seed = null)
    {
        _source = source;
        _seed = seed;
        _shuffled = new Lazy<IDataHandle>(() => Shuffle());
    }

    public ISchema Schema => _source.Schema;
    public long? RowCount => _source.RowCount;
    public OrderingPolicy Ordering => OrderingPolicy.Unordered;
    public MaterializationCapabilities Capabilities => _shuffled.Value.Capabilities;

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => _shuffled.Value.GetCursor(columnsNeeded);

    public IDataHandle Materialize() => _shuffled.Value;

    public IAsyncEnumerable<IRow> StreamRows(CancellationToken ct = default)
        => _shuffled.Value.StreamRows(ct);

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
        => _shuffled.Value.TryGetColumnBatch(columnName, startRow, rowCount, out batch);

    private IDataHandle Shuffle()
    {
        var materialized = _source.Materialize();
        var rowCount = (int)(materialized.RowCount ?? 0);
        var rng = _seed.HasValue ? new Random(_seed.Value) : Random.Shared;
        var indices = Enumerable.Range(0, rowCount).ToArray();
        rng.Shuffle(indices);
        return new IndexedDataHandle(materialized, indices).Materialize();
    }
}
