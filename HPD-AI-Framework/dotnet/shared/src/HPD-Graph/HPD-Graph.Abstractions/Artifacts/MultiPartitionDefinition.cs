using System.IO.Hashing;
using System.Text;

namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Multi-dimensional partitioning (Cartesian product of multiple dimensions).
/// Example: daily × region → ["2025-01-15", "us-west"], ["2025-01-15", "eu-central"], etc.
/// Generates all combinations of partition keys from each dimension.
///
/// IMPORTANT: This enables composing dynamic and static partitions.
/// Example: TimePartitionDefinition.Daily() × DatabaseQueryPartitionDefinition()
/// The orchestrator will resolve each dimension asynchronously and compute the Cartesian product.
/// </summary>
public record MultiPartitionDefinition : PartitionDefinition
{
    /// <summary>
    /// List of partition dimensions to combine.
    /// The Cartesian product of all dimension keys is generated.
    /// </summary>
    public required IReadOnlyList<PartitionDefinition> Dimensions { get; init; }

    public override async Task<PartitionSnapshot> ResolveAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Resolve all dimensions (supports dynamic dimensions!)
        var dimensionSnapshots = new List<PartitionSnapshot>();
        foreach (var dimension in Dimensions)
        {
            var snapshot = await dimension.ResolveAsync(services, cancellationToken);
            dimensionSnapshots.Add(snapshot);
        }

        // Compute Cartesian product of all dimension keys
        var cartesianKeys = ComputeCartesianProduct(
            dimensionSnapshots.Select(s => s.Keys).ToList()
        ).ToList();

        // Composite hash: combine all dimension snapshot hashes
        // This ensures that if ANY dimension changes, the multi-partition hash changes
        var compositeHash = ComputeCompositeHash(dimensionSnapshots);

        return new PartitionSnapshot
        {
            Keys = cartesianKeys,
            SnapshotHash = compositeHash
        };
    }

    /// <summary>
    /// Compute composite hash from all dimension snapshot hashes.
    /// Uses XxHash64 to combine individual dimension hashes deterministically.
    /// </summary>
    private static string ComputeCompositeHash(List<PartitionSnapshot> snapshots)
    {
        var hash = new XxHash64();

        // Include type discriminator
        hash.Append(Encoding.UTF8.GetBytes("Multi"));

        // Include each dimension's snapshot hash
        foreach (var snapshot in snapshots)
        {
            hash.Append(Encoding.UTF8.GetBytes(snapshot.SnapshotHash));
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    /// <summary>
    /// Compute Cartesian product of partition keys from multiple dimensions.
    /// Returns all possible combinations as multi-dimensional partition keys.
    /// </summary>
    private static IEnumerable<PartitionKey> ComputeCartesianProduct(
        List<IReadOnlyList<PartitionKey>> dimensions)
    {
        if (dimensions.Count == 0)
            yield break;

        // Start with first dimension's keys
        IEnumerable<IEnumerable<string>> result = dimensions[0]
            .SelectMany(pk => pk.Dimensions)
            .Select(k => new[] { k });

        // Combine with each subsequent dimension
        for (int i = 1; i < dimensions.Count; i++)
        {
            var dimension = dimensions[i];
            result = from combination in result
                     from partitionKey in dimension
                     from dimensionValue in partitionKey.Dimensions
                     select combination.Concat(new[] { dimensionValue });
        }

        // Convert to PartitionKey objects
        foreach (var dimensionValues in result)
        {
            yield return new PartitionKey { Dimensions = dimensionValues.ToList() };
        }
    }

    /// <summary>
    /// Compute Cartesian product of multiple lists.
    /// </summary>
    private static IEnumerable<IEnumerable<string>> CartesianProduct(List<List<PartitionKey>> dimensions)
    {
        if (dimensions.Count == 0)
        {
            yield return Enumerable.Empty<string>();
            yield break;
        }

        // Start with first dimension's keys
        IEnumerable<IEnumerable<string>> result = dimensions[0]
            .SelectMany(pk => pk.Dimensions)
            .Select(k => new[] { k });

        // Combine with each subsequent dimension
        for (int i = 1; i < dimensions.Count; i++)
        {
            var dimension = dimensions[i];
            result = from combination in result
                     from partitionKey in dimension
                     from dimensionValue in partitionKey.Dimensions
                     select combination.Concat(new[] { dimensionValue });
        }

        foreach (var item in result)
        {
            yield return item;
        }
    }

    /// <summary>
    /// Factory: Create multi-dimensional partitions from two dimensions.
    /// </summary>
    public static MultiPartitionDefinition Combine(PartitionDefinition dimension1, PartitionDefinition dimension2)
    {
        return new MultiPartitionDefinition
        {
            Dimensions = new[] { dimension1, dimension2 }
        };
    }

    /// <summary>
    /// Factory: Create multi-dimensional partitions from multiple dimensions.
    /// </summary>
    public static MultiPartitionDefinition Combine(params PartitionDefinition[] dimensions)
    {
        if (dimensions == null || dimensions.Length < 2)
            throw new ArgumentException("At least two dimensions are required for multi-partitioning", nameof(dimensions));

        return new MultiPartitionDefinition { Dimensions = dimensions };
    }

    /// <summary>
    /// Factory: Create multi-dimensional partitions from a list of dimensions.
    /// </summary>
    public static MultiPartitionDefinition Combine(IEnumerable<PartitionDefinition> dimensions)
    {
        var dimensionList = dimensions.ToList();
        if (dimensionList.Count < 2)
            throw new ArgumentException("At least two dimensions are required for multi-partitioning", nameof(dimensions));

        return new MultiPartitionDefinition { Dimensions = dimensionList };
    }
}
