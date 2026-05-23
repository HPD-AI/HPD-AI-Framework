namespace HPD.ML.Backends.Pjrt;

public sealed record PjrtClientCreateOptions
{
    public string? PlatformName { get; init; }
    public string? Allocator { get; init; }
    public float? MemoryFraction { get; init; }
    public bool? Preallocate { get; init; }
    public long? CollectiveMemorySize { get; init; }
    public IReadOnlyList<long>? VisibleDevices { get; init; }
    public long? NodeId { get; init; }
    public long? NumNodes { get; init; }
    public bool? ShouldStageHostToDeviceTransfers { get; init; }
    public bool? AbortCollectivesOnFailure { get; init; }
    public bool? UseTfrtGpuClient { get; init; }
    public bool? EnableMockNccl { get; init; }
    public string? MockGpuTopology { get; init; }
    public long? PartitionIndex { get; init; }
}
