namespace HPD.Agent;

public enum ThreadForkCompactionIntent
{
    Inherit,
    Enabled,
    Disabled,
    PreferCache
}

public sealed record ThreadForkOptions
{
    public Dictionary<string, object>? Metadata { get; init; }

    public ThreadForkCompactionIntent CompactionIntent { get; init; } =
        ThreadForkCompactionIntent.Inherit;

    public static ThreadForkOptions Default { get; } = new();

    public static ThreadForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new ThreadForkOptions { Metadata = metadata };
}
