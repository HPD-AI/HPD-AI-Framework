namespace HPD.Agent;

public enum ThreadForkCompactionMode
{
    Inherit,
    Enabled,
    Disabled
}

public sealed record ThreadForkCompactionOptions
{
    public ThreadForkCompactionMode Mode { get; init; } = ThreadForkCompactionMode.Inherit;


    public CompactionStrategyOptions? Strategy { get; init; }

    public static ThreadForkCompactionOptions Inherit { get; } = new();

    public static ThreadForkCompactionOptions Enabled { get; } = new()
    {
        Mode = ThreadForkCompactionMode.Enabled
    };

    public static ThreadForkCompactionOptions Disabled { get; } = new()
    {
        Mode = ThreadForkCompactionMode.Disabled
    };
}

public sealed record ThreadForkOptions
{
    public Dictionary<string, object>? Metadata { get; init; }

    public ThreadForkCompactionOptions? Compaction { get; init; }

    public static ThreadForkOptions Default { get; } = new();

    public static ThreadForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new ThreadForkOptions { Metadata = metadata };
}
