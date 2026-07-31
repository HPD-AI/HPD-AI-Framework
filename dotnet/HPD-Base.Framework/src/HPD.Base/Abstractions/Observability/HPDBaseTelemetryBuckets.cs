namespace HPD.Base;

/// <summary>
/// Provides deterministic bucket helpers for safe telemetry dimensions.
/// </summary>
public static class HPDBaseTelemetryBuckets
{
    /// <summary>Returns a bounded page-size bucket.</summary>
    /// <param name="value">The page size, or <see langword="null" /> when absent.</param>
    /// <returns>A low-cardinality page-size bucket.</returns>
    public static string PageSize(int? value) => value switch
    {
        null => "none",
        <= 0 => "invalid",
        <= 25 => "1-25",
        <= 100 => "26-100",
        <= 500 => "101-500",
        _ => "gt500"
    };

    /// <summary>Returns a bounded file-size bucket.</summary>
    /// <param name="value">The byte count, or <see langword="null" /> when unknown.</param>
    /// <returns>A low-cardinality file-size bucket.</returns>
    public static string FileSize(long? value) => ByteSize(value);

    /// <summary>Returns a bounded payload-size bucket.</summary>
    /// <param name="value">The byte count, or <see langword="null" /> when unknown.</param>
    /// <returns>A low-cardinality payload-size bucket.</returns>
    public static string PayloadSize(long? value) => ByteSize(value);

    /// <summary>Returns a bounded count bucket.</summary>
    /// <param name="value">The count, or <see langword="null" /> when unknown.</param>
    /// <returns>A low-cardinality count bucket.</returns>
    public static string Count(int? value) => value switch
    {
        null => "unknown",
        < 0 => "invalid",
        0 => "0",
        1 => "1",
        <= 5 => "2-5",
        <= 25 => "6-25",
        <= 100 => "26-100",
        _ => "gt100"
    };

    private static string ByteSize(long? value) => value switch
    {
        null => "unknown",
        < 0 => "invalid",
        0 => "0",
        <= 1024 => "1-1KiB",
        <= 1024 * 1024 => "1KiB-1MiB",
        <= 100L * 1024 * 1024 => "1MiB-100MiB",
        _ => "gt100MiB"
    };
}
