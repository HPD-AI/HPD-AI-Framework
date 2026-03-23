namespace HPD.RAG.Evaluation;

/// <summary>
/// An immutable, multi-segment key that identifies a (scenario, iteration) partition
/// in an evaluation result store.
/// </summary>
/// <remarks>
/// Mirrors the <c>PartitionKey</c> concept used by the Microsoft.Extensions.AI.Evaluation
/// reporting infrastructure.  The segments are ordered and compared positionally.
/// </remarks>
public sealed class PartitionKey : IEquatable<PartitionKey>
{
    /// <summary>Gets the ordered segments that make up this partition key.</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>Initialises a <see cref="PartitionKey"/> from the supplied segments.</summary>
    /// <param name="segments">One or more strings that together identify the partition.</param>
    public PartitionKey(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments.ToArray();
    }

    /// <inheritdoc/>
    public bool Equals(PartitionKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Segments.Count != other.Segments.Count) return false;
        for (int i = 0; i < Segments.Count; i++)
        {
            if (!string.Equals(Segments[i], other.Segments[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PartitionKey);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var s in Segments) hc.Add(s, StringComparer.Ordinal);
        return hc.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => string.Join("/", Segments);
}
