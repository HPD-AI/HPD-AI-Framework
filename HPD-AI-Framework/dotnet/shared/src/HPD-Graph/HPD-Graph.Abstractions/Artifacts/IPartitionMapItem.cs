namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Marker interface for partition-aware map items.
/// Used by Map execution loop to identify partition items and set CurrentPartition on context.
/// </summary>
public interface IPartitionMapItem
{
    /// <summary>
    /// The partition key for this map item.
    /// </summary>
    PartitionKey PartitionKey { get; }
}

/// <summary>
/// Concrete partition map item implementation.
/// Used when converting partitions to Map items for execution.
/// </summary>
public record PartitionMapItem(int Index, PartitionKey PartitionKey) : IPartitionMapItem;
