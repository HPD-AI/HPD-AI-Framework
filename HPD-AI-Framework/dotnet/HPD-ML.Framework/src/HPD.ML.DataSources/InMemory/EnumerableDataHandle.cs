namespace HPD.ML.DataSources;

using HPD.ML.Core;

/// <summary>
/// Creates an InMemoryDataHandle from IEnumerable&lt;T&gt; using
/// a user-provided schema mapper. AOT-safe — no reflection.
/// </summary>
/// <remarks>
/// Delegates to <see cref="InMemoryDataHandle.FromEnumerable{T}"/>.
/// This class exists for discoverability via the DataSources namespace.
/// </remarks>
public static class EnumerableDataHandle
{
    /// <summary>
    /// Create from an enumerable with explicit property extraction.
    /// </summary>
    public static InMemoryDataHandle Create<T>(
        IEnumerable<T> items,
        Schema schema,
        Func<T, Dictionary<string, object>> extractor)
        => InMemoryDataHandle.FromEnumerable(items, extractor, schema);
}
