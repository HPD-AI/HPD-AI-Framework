namespace HPD.ML.DataSources;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// IDataHandle backed by an Apache Parquet file.
/// Supports zero-copy columnar batch access via TryGetColumnBatch.
/// Schema is read from Parquet metadata (no inference scan needed).
/// </summary>
/// <remarks>
/// Implementation requires Parquet.Net dependency. Core methods are stubbed
/// with NotImplementedException until the dependency is added.
/// </remarks>
public sealed class ParquetDataHandle : IDataHandle
{
    private readonly string _path;
    private readonly ParquetOptions _options;
    private readonly Schema _schema;
    private readonly long _rowCount;
    private Dictionary<string, Array>? _columnCache;
    private readonly object _cacheLock = new();

    private ParquetDataHandle(string path, ParquetOptions options, Schema schema, long rowCount)
    {
        _path = path;
        _options = options;
        _schema = schema;
        _rowCount = rowCount;
    }

    public ISchema Schema => _schema;
    public long? RowCount => _rowCount;
    public OrderingPolicy Ordering => OrderingPolicy.StrictlyOrdered;

    public MaterializationCapabilities Capabilities
        => MaterializationCapabilities.ColumnarAccess
         | MaterializationCapabilities.BatchAccess
         | MaterializationCapabilities.KnownDensity;

    /// <summary>
    /// Create a Parquet data handle. Reads schema from file metadata.
    /// </summary>
    public static ParquetDataHandle Create(string path, ParquetOptions? options = null)
    {
        options ??= new ParquetOptions();
        var (schema, rowCount) = ReadParquetMetadata(path, options);
        return new ParquetDataHandle(path, options, schema, rowCount);
    }

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
    {
        EnsureLoaded();
        return new ArrayRowCursor(_schema, _columnCache!, columnsNeeded.ToArray(), _rowCount);
    }

    public IDataHandle Materialize()
    {
        EnsureLoaded();
        return new InMemoryDataHandle(_schema, _columnCache!);
    }

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureLoaded();
        for (long i = 0; i < _rowCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return new Row(_schema, _columnCache!, i);
        }
    }

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
    {
        EnsureLoaded();
        if (_columnCache!.TryGetValue(columnName, out var array) && array is T[] typed)
        {
            int count = Math.Min(rowCount, typed.Length - startRow);
            if (count <= 0) { batch = default; return false; }
            batch = TensorHelpers.AsScalarBatch(typed, startRow, count);
            return true;
        }
        batch = default;
        return false;
    }

    private void EnsureLoaded()
    {
        if (_columnCache is not null) return;
        lock (_cacheLock)
        {
            if (_columnCache is not null) return;
            _columnCache = ReadParquetColumns(_path, _schema, _options);
        }
    }

    private static (Schema schema, long rowCount) ReadParquetMetadata(string path, ParquetOptions options)
    {
        // Requires Parquet.Net dependency.
        // Schema maps: INT32→int, INT64→long, FLOAT→float, DOUBLE→double,
        // BYTE_ARRAY→string, BOOLEAN→bool.
        throw new NotImplementedException(
            "Parquet metadata reading requires Parquet.Net dependency.");
    }

    private static Dictionary<string, Array> ReadParquetColumns(
        string path, Schema schema, ParquetOptions options)
    {
        // Requires Parquet.Net dependency.
        // Each Parquet column becomes a typed array.
        // Column projection via options.Columns.
        // Row group selection via options.RowGroups.
        throw new NotImplementedException(
            "Parquet column reading requires Parquet.Net dependency.");
    }
}
