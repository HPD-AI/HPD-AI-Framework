using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

/// <summary>Identifies the direction in which a graph item travels.</summary>
public enum GraphDirectionV1 : ushort
{
    /// <summary>Moves input toward the conversation and its providers.</summary>
    IngressForward = 1,
    /// <summary>Moves generated output toward a sink.</summary>
    EgressForward = 2,
    /// <summary>Moves feedback or acknowledgements toward an earlier stage.</summary>
    UpstreamFeedback = 3,
    /// <summary>Moves a compiler-scoped broadcast to its declared recipients.</summary>
    CompilerBroadcast = 4
}

/// <summary>Identifies the scheduling and ordering domain of a graph item.</summary>
public enum GraphTrafficDomainV1 : ushort
{
    /// <summary>Contains media payload work.</summary>
    Media = 1,
    /// <summary>Contains ordinary non-media data.</summary>
    OrdinaryData = 2,
    /// <summary>Contains evidence and receipts.</summary>
    Evidence = 3,
    /// <summary>Contains lifecycle or barrier work.</summary>
    LifecycleBarrier = 4,
    /// <summary>Contains urgent control work.</summary>
    UrgentControl = 5
}

/// <summary>Represents an unsigned position within one graph scope.</summary>
/// <remarks>The value is meaningful only with a session, graph generation, direction, and traffic domain.</remarks>
public readonly record struct GraphFramePositionV1
{
    /// <summary>Initializes a graph frame position.</summary>
    /// <param name="value">The zero-based unsigned position.</param>
    public GraphFramePositionV1(ulong value) => Value = value;

    /// <summary>Gets the zero-based unsigned position.</summary>
    public ulong Value { get; }
}

/// <summary>Represents a validated half-open range within one graph scope.</summary>
public readonly record struct GraphMediaRangeV1
{
    /// <summary>Gets the maximum number of positions represented by one range.</summary>
    public const uint MaximumCount = 1_048_576;

    /// <summary>Gets the maximum encoded bytes attributed to one range.</summary>
    public const ulong MaximumEncodedBytes = 16_777_216;

    /// <summary>Gets the maximum declared media duration in nanoseconds.</summary>
    public const long MaximumMediaDurationNanoseconds = 60_000_000_000;

    /// <summary>Initializes a validated graph media range.</summary>
    /// <param name="session">The S1 session authority scope.</param>
    /// <param name="graphGeneration">The S2 graph generation.</param>
    /// <param name="direction">The graph direction.</param>
    /// <param name="domain">The scheduling and ordering domain.</param>
    /// <param name="start">The first included position.</param>
    /// <param name="count">The positive number of included positions.</param>
    /// <param name="encodedBytes">The bounded encoded bytes attributed to the range.</param>
    /// <param name="mediaDuration">The bounded declared media duration.</param>
    /// <exception cref="ArgumentException">The session, graph generation, direction, or domain is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A bound is violated or the half-open end overflows.</exception>
    public GraphMediaRangeV1(SessionAuthorityStampV1 session, GraphGenerationId graphGeneration,
        GraphDirectionV1 direction, GraphTrafficDomainV1 domain, GraphFramePositionV1 start,
        uint count, ulong encodedBytes, DurationNs mediaDuration)
    {
        if (!session.IsValid)
            throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        if (!graphGeneration.IsValid)
            throw new ArgumentException("A valid graph generation is required.", nameof(graphGeneration));
        if (!IsRegistered(direction))
            throw new ArgumentException("The graph direction is not registered.", nameof(direction));
        if (!IsRegistered(domain))
            throw new ArgumentException("The graph traffic domain is not registered.", nameof(domain));
        if (count is 0 or > MaximumCount)
            throw new ArgumentOutOfRangeException(nameof(count), $"Count must be between 1 and {MaximumCount}.");
        if (encodedBytes > MaximumEncodedBytes)
            throw new ArgumentOutOfRangeException(nameof(encodedBytes), $"Encoded bytes cannot exceed {MaximumEncodedBytes}.");
        if (mediaDuration.Nanoseconds is < 0 or > MaximumMediaDurationNanoseconds)
            throw new ArgumentOutOfRangeException(nameof(mediaDuration),
                $"Media duration must be between 0 and {MaximumMediaDurationNanoseconds} nanoseconds.");
        if (start.Value > ulong.MaxValue - count)
            throw new ArgumentOutOfRangeException(nameof(start), "The start plus count exceeds UInt64.");

        Session = session;
        GraphGeneration = graphGeneration;
        Direction = direction;
        Domain = domain;
        Start = start;
        Count = count;
        EncodedBytes = encodedBytes;
        MediaDuration = mediaDuration;
    }

    /// <summary>Gets the S1 session authority scope.</summary>
    public SessionAuthorityStampV1 Session { get; }
    /// <summary>Gets the S2 graph generation.</summary>
    public GraphGenerationId GraphGeneration { get; }
    /// <summary>Gets the graph direction.</summary>
    public GraphDirectionV1 Direction { get; }
    /// <summary>Gets the scheduling and ordering domain.</summary>
    public GraphTrafficDomainV1 Domain { get; }
    /// <summary>Gets the first included position.</summary>
    public GraphFramePositionV1 Start { get; }
    /// <summary>Gets the number of included positions.</summary>
    public uint Count { get; }
    /// <summary>Gets the encoded bytes attributed to the range.</summary>
    public ulong EncodedBytes { get; }
    /// <summary>Gets the declared media duration.</summary>
    public DurationNs MediaDuration { get; }
    /// <summary>Gets whether this value satisfies every required identity, enum, and numeric bound.</summary>
    public bool IsValid => Session.IsValid && GraphGeneration.IsValid && IsRegistered(Direction) && IsRegistered(Domain) &&
        Count is > 0 and <= MaximumCount && EncodedBytes <= MaximumEncodedBytes &&
        MediaDuration.Nanoseconds is >= 0 and <= MaximumMediaDurationNanoseconds &&
        Start.Value <= ulong.MaxValue - Count;
    /// <summary>Gets the first excluded position.</summary>
    public GraphFramePositionV1 EndExclusive => new(Start.Value + Count);

    /// <summary>Returns whether another range has the same authority and ordering scope.</summary>
    /// <param name="other">The range to compare.</param>
    /// <returns><see langword="true"/> when all four scope fields are equal.</returns>
    public bool HasSameScope(GraphMediaRangeV1 other) =>
        IsValid && other.IsValid && Session == other.Session && GraphGeneration == other.GraphGeneration &&
        Direction == other.Direction && Domain == other.Domain;

    /// <summary>Returns whether another range begins exactly at this range's end in the same scope.</summary>
    /// <param name="other">The candidate following range.</param>
    /// <returns><see langword="true"/> when the ranges are same-scoped and exactly adjacent.</returns>
    public bool IsImmediatelyBefore(GraphMediaRangeV1 other) =>
        HasSameScope(other) && EndExclusive == other.Start;

    private static bool IsRegistered(GraphDirectionV1 value) => value is GraphDirectionV1.IngressForward
        or GraphDirectionV1.EgressForward or GraphDirectionV1.UpstreamFeedback or GraphDirectionV1.CompilerBroadcast;

    private static bool IsRegistered(GraphTrafficDomainV1 value) => value is GraphTrafficDomainV1.Media
        or GraphTrafficDomainV1.OrdinaryData or GraphTrafficDomainV1.Evidence
        or GraphTrafficDomainV1.LifecycleBarrier or GraphTrafficDomainV1.UrgentControl;
}
