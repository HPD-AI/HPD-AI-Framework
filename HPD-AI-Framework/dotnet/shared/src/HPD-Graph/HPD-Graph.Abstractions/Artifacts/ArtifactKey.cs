namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Hierarchical identifier for a data artifact.
/// Examples:
///   ["database", "users"]                  → database/users
///   ["warehouse", "dim_users"]             → warehouse/dim_users
///   ["database", "users"] @ "2025-01-15"   → database/users@2025-01-15 (partitioned)
/// </summary>
public record ArtifactKey
{
    /// <summary>
    /// Hierarchical path segments identifying the artifact.
    /// Example: ["database", "schema", "table"]
    /// </summary>
    public required IReadOnlyList<string> Path { get; init; }

    /// <summary>
    /// Optional partition key for partitioned artifacts.
    /// Null for non-partitioned artifacts.
    /// </summary>
    public PartitionKey? Partition { get; init; }

    /// <summary>
    /// String representation using forward slash as path separator and @ for partition.
    /// Examples:
    ///   "database/users"
    ///   "database/users@2025-01-15"
    ///   "warehouse/dim_users@2025-01-15|us-west"
    /// </summary>
    public override string ToString() =>
        Partition != null
            ? $"{string.Join("/", Path)}@{Partition}"
            : string.Join("/", Path);

    /// <summary>
    /// Parse an artifact key from string format.
    /// Format: "path/segments[@partition]"
    /// Examples:
    ///   "database/users"                    → Path=["database", "users"], Partition=null
    ///   "database/users@2025-01-15"         → Path=["database", "users"], Partition="2025-01-15"
    ///   "warehouse/dim@2025-01-15|us-west"  → Path=["warehouse", "dim"], Partition=["2025-01-15", "us-west"]
    /// </summary>
    public static ArtifactKey Parse(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Artifact key cannot be empty", nameof(key));

        // Split on @ to separate path from partition
        var parts = key.Split('@', 2);
        var pathString = parts[0];
        var partitionString = parts.Length > 1 ? parts[1] : null;

        // Parse path
        var pathSegments = pathString.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 0)
            throw new ArgumentException("Artifact key must have at least one path segment", nameof(key));

        // Parse partition if present
        var partition = partitionString != null ? PartitionKey.Parse(partitionString) : null;

        return new ArtifactKey
        {
            Path = pathSegments,
            Partition = partition
        };
    }

    /// <summary>
    /// Create an artifact key from path segments without partition.
    /// </summary>
    public static ArtifactKey FromPath(params string[] pathSegments)
    {
        if (pathSegments == null || pathSegments.Length == 0)
            throw new ArgumentException("Path must have at least one segment", nameof(pathSegments));

        return new ArtifactKey { Path = pathSegments };
    }

    /// <summary>
    /// Create an artifact key from path segments with partition.
    /// </summary>
    public static ArtifactKey FromPath(IReadOnlyList<string> pathSegments, PartitionKey partition)
    {
        if (pathSegments == null || pathSegments.Count == 0)
            throw new ArgumentException("Path must have at least one segment", nameof(pathSegments));

        return new ArtifactKey { Path = pathSegments, Partition = partition };
    }

    /// <summary>
    /// Equality comparison based on path and partition.
    /// </summary>
    public virtual bool Equals(ArtifactKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Path.Count != other.Path.Count) return false;

        for (int i = 0; i < Path.Count; i++)
        {
            if (Path[i] != other.Path[i])
                return false;
        }

        return Equals(Partition, other.Partition);
    }

    /// <summary>
    /// Hash code based on path and partition.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in Path)
        {
            hash.Add(segment);
        }
        hash.Add(Partition);
        return hash.ToHashCode();
    }
}
