namespace HPD.Agent;

public enum BranchForkCompactionIntent
{
    Inherit,
    Enabled,
    Disabled
}

public sealed record BranchForkOptions
{
    public Dictionary<string, object>? Metadata { get; init; }

    public BranchForkCompactionIntent CompactionIntent { get; init; } =
        BranchForkCompactionIntent.Inherit;

    public static BranchForkOptions Default { get; } = new();

    public static BranchForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new BranchForkOptions { Metadata = metadata };
}
