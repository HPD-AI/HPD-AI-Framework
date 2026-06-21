namespace HPD.ML.DataSources;

/// <summary>
/// Configuration for Parquet loading.
/// </summary>
public sealed record ParquetOptions
{
    /// <summary>
    /// Specific columns to read. Default: null (all columns).
    /// Column projection is pushed down to the Parquet reader for efficiency.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Row group indices to read. Default: null (all row groups).
    /// Useful for partitioned processing.
    /// </summary>
    public IReadOnlyList<int>? RowGroups { get; init; }

    /// <summary>
    /// Batch size for columnar reads. Default: 4096.
    /// </summary>
    public int BatchSize { get; init; } = 4096;
}
