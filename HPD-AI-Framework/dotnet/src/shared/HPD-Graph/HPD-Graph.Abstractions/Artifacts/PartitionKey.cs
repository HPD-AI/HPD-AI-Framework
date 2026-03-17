namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// A partition key identifying a slice of data.
/// Can be simple (single dimension) or composite (multi-dimensional).
/// Examples:
///   "2025-01-15"                    (daily partition)
///   "us-west"                       (regional partition)
///   ["2025-01-15", "us-west"]      (multi-dimensional: date × region)
/// </summary>
public record PartitionKey
{
    /// <summary>
    /// The dimensions of this partition key.
    /// Single-dimensional keys have one element, multi-dimensional have multiple.
    /// </summary>
    public required IReadOnlyList<string> Dimensions { get; init; }

    /// <summary>
    /// Implicit conversion from string to single-dimension partition key.
    /// </summary>
    public static implicit operator PartitionKey(string key) => new() { Dimensions = [key] };

    /// <summary>
    /// String representation using pipe (|) as dimension separator.
    /// Example: "2025-01-15|us-west"
    /// </summary>
    public override string ToString() => string.Join("|", Dimensions);

    /// <summary>
    /// Parse a partition key from string format.
    /// Single dimension: "2025-01-15"
    /// Multi dimension: "2025-01-15|us-west"
    /// </summary>
    public static PartitionKey Parse(string str)
    {
        if (string.IsNullOrWhiteSpace(str))
            throw new ArgumentException("Partition key cannot be empty", nameof(str));

        var dimensions = str.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (dimensions.Length == 0)
            throw new ArgumentException("Partition key must have at least one dimension", nameof(str));

        return new PartitionKey { Dimensions = dimensions };
    }

    /// <summary>
    /// Equality comparison based on dimension values.
    /// </summary>
    public virtual bool Equals(PartitionKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Dimensions.Count != other.Dimensions.Count) return false;

        for (int i = 0; i < Dimensions.Count; i++)
        {
            if (Dimensions[i] != other.Dimensions[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Hash code based on dimension values.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var dimension in Dimensions)
        {
            hash.Add(dimension);
        }
        return hash.ToHashCode();
    }
}
