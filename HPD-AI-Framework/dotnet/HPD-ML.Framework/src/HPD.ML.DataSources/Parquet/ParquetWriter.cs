namespace HPD.ML.DataSources;

using HPD.ML.Abstractions;

/// <summary>
/// Writes any IDataHandle to Apache Parquet format.
/// Prefers columnar batch path when available for zero-copy writes.
/// </summary>
public static class ParquetWriter
{
    /// <summary>
    /// Write a DataHandle to Parquet. If the source supports TryGetColumnBatch,
    /// writes directly from tensor spans (zero-copy). Otherwise falls back to cursor.
    /// </summary>
    public static void Write(IDataHandle data, string path, int rowGroupSize = 4096)
    {
        // 1. Map ISchema columns to Parquet schema
        // 2. If source has BatchAccess capability, use TryGetColumnBatch for zero-copy
        // 3. Otherwise, cursor through and buffer into row groups
        throw new NotImplementedException(
            "Parquet writing requires Parquet.Net dependency.");
    }
}
