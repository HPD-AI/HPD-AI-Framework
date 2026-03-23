using System.Numerics;
using System.Numerics.Tensors;

namespace HPD.ML.Abstractions;

/// <summary>
/// A format-agnostic, lazy reference to schema-bearing data.
/// Consumers choose their materialization strategy.
/// </summary>
public interface IDataHandle
{
    /// <summary>The schema describing columns, types, and annotations.</summary>
    ISchema Schema { get; }

    /// <summary>Total row count, or null if unknown (streaming/lazy source).</summary>
    long? RowCount { get; }

    /// <summary>Ordering guarantee provided by this handle.</summary>
    OrderingPolicy Ordering { get; }

    /// <summary>Reports which fast-path materialization modes are available.</summary>
    MaterializationCapabilities Capabilities { get; }

    /// <summary>Forward-only, row-by-row cursor. Universal baseline.</summary>
    IRowCursor GetCursor(IEnumerable<string> columnsNeeded);

    /// <summary>Fully-materialized, in-memory copy with columnar access.</summary>
    IDataHandle Materialize();

    /// <summary>Async row stream with ordering and cancellation.</summary>
    IAsyncEnumerable<IRow> StreamRows(CancellationToken ct = default);

    /// <summary>
    /// Zero-copy tensor view of a typed column batch.
    /// Returns false if unavailable (caller falls back to cursor).
    /// For scalar columns: shape [rowCount]. For vector columns: shape [rowCount, ...dims].
    /// </summary>
    bool TryGetColumnBatch<T>(
        string columnName,
        int startRow,
        int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>;
}
