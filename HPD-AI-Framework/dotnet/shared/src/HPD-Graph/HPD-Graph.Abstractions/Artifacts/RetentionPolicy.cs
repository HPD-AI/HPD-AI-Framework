namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Defines retention rules for artifact versions.
/// Prevents unbounded growth of artifact registry in production.
/// Can combine multiple retention criteria (all must be satisfied).
/// </summary>
public record RetentionPolicy
{
    /// <summary>
    /// Keep last N versions (null = unlimited).
    /// Example: KeepLastN = 10 → retain only 10 most recent versions.
    /// </summary>
    public int? KeepLastN { get; init; }

    /// <summary>
    /// Keep versions newer than this duration (null = unlimited).
    /// Example: KeepNewerThan = 30 days → delete versions older than 30 days.
    /// </summary>
    public TimeSpan? KeepNewerThan { get; init; }

    /// <summary>
    /// Custom predicate for version retention.
    /// Example: Keep versions tagged with "production" = true.
    /// Evaluated against ArtifactMetadata.
    /// </summary>
    public Func<ArtifactMetadata, bool>? KeepIf { get; init; }

    /// <summary>
    /// Factory: Keep last N versions only.
    /// </summary>
    public static RetentionPolicy KeepLast(int n)
    {
        if (n <= 0)
            throw new ArgumentException("Must keep at least 1 version", nameof(n));

        return new RetentionPolicy { KeepLastN = n };
    }

    /// <summary>
    /// Factory: Keep versions newer than specified duration.
    /// </summary>
    public static RetentionPolicy KeepRecent(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be positive", nameof(duration));

        return new RetentionPolicy { KeepNewerThan = duration };
    }

    /// <summary>
    /// Factory: Combine multiple retention criteria (AND logic).
    /// A version is kept if it satisfies ALL criteria.
    /// </summary>
    public static RetentionPolicy Combine(int keepLastN, TimeSpan keepNewerThan)
    {
        if (keepLastN <= 0)
            throw new ArgumentException("Must keep at least 1 version", nameof(keepLastN));
        if (keepNewerThan <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be positive", nameof(keepNewerThan));

        return new RetentionPolicy
        {
            KeepLastN = keepLastN,
            KeepNewerThan = keepNewerThan
        };
    }

    /// <summary>
    /// Default retention policy: Keep last 10 versions OR 30 days (whichever is more).
    /// Recommended for production use.
    /// </summary>
    public static RetentionPolicy Default => new()
    {
        KeepLastN = 10,
        KeepNewerThan = TimeSpan.FromDays(30)
    };
}
