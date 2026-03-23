using System.IO.Hashing;
using System.Text;

namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Static set of partition keys (regions, departments, categories, etc.).
/// Partition keys are explicitly defined and do not change based on time.
///
/// Examples:
/// - Regional: ["us-east", "us-west", "eu-central"]
/// - Departmental: ["sales", "marketing", "engineering"]
/// - Categorical: ["bronze", "silver", "gold"]
///
/// Static partitions produce deterministic snapshots - same keys always produce
/// same snapshot hash, enabling stable incremental execution.
/// </summary>
public record StaticPartitionDefinition : PartitionDefinition
{
    /// <summary>
    /// Fixed set of partition keys.
    /// Examples: ["us-east", "us-west", "eu-central"], ["sales", "marketing", "engineering"]
    /// </summary>
    public required IReadOnlyList<string> Keys { get; init; }

    public override Task<PartitionSnapshot> ResolveAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Convert string keys to PartitionKey objects
        var partitionKeys = Keys
            .Select(k => new PartitionKey { Dimensions = new[] { k } })
            .ToList();

        // Compute deterministic hash: same keys = same hash (fully stable)
        // This enables incremental execution - changing the key set invalidates the hash
        var snapshotHash = ComputeStableHash(Keys);

        var snapshot = new PartitionSnapshot
        {
            Keys = partitionKeys,
            SnapshotHash = snapshotHash
        };

        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// Compute stable hash from partition keys.
    /// Uses XxHash64 for fast, deterministic hashing.
    /// Hash includes all keys in order, so changing the key set changes the hash.
    /// </summary>
    private static string ComputeStableHash(IReadOnlyList<string> keys)
    {
        var hash = new XxHash64();

        // Include type discriminator to avoid collisions with other partition types
        hash.Append(Encoding.UTF8.GetBytes("Static"));

        // Hash each key in order
        foreach (var key in keys)
        {
            hash.Append(Encoding.UTF8.GetBytes(key));
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    /// <summary>
    /// Factory: Create static partitions from a list of keys.
    /// </summary>
    public static StaticPartitionDefinition FromKeys(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            throw new ArgumentException("At least one partition key is required", nameof(keys));

        return new StaticPartitionDefinition { Keys = keys };
    }

    /// <summary>
    /// Factory: Create static partitions from a list of keys.
    /// </summary>
    public static StaticPartitionDefinition FromKeys(IEnumerable<string> keys)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
            throw new ArgumentException("At least one partition key is required", nameof(keys));

        return new StaticPartitionDefinition { Keys = keyList };
    }
}
