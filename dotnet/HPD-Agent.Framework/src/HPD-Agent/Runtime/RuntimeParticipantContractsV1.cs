using HPD.Agent.Authority;

namespace HPD.Agent.Runtime;

/// <summary>Describes the closed result of one neutral runtime-participant lifecycle operation.</summary>
public enum RuntimeParticipantDispositionV1 : ushort
{
    /// <summary>The requested lifecycle transition completed.</summary>
    Succeeded = 1,
    /// <summary>The participant refused the transition without performing an external effect.</summary>
    Refused = 2,
    /// <summary>The participant reported a bounded failure.</summary>
    Failed = 3,
    /// <summary>The transition exceeded its declared bound.</summary>
    TimedOut = 4,
    /// <summary>The caller cancelled the transition.</summary>
    Cancelled = 5,
}

/// <summary>Describes whether admitted work should converge gracefully or under a forced bound.</summary>
public enum RuntimeDrainIntentV1 : ushort
{
    /// <summary>Stop new admission and settle already admitted work.</summary>
    Graceful = 1,
    /// <summary>Stop new admission and converge within the forced termination bound.</summary>
    Forced = 2,
}

/// <summary>Describes why a participant is being terminated.</summary>
public enum RuntimeTerminationCauseV1 : ushort
{
    /// <summary>The owning runtime requested ordinary termination.</summary>
    Requested = 1,
    /// <summary>A participant failed or refused preparation.</summary>
    PrepareFailed = 2,
    /// <summary>A participant failed or refused start.</summary>
    StartFailed = 3,
    /// <summary>Drain did not converge successfully.</summary>
    DrainFailed = 4,
    /// <summary>The owning operation was cancelled.</summary>
    Cancelled = 5,
    /// <summary>A lifecycle transition exceeded its declared bound.</summary>
    TimedOut = 6,
    /// <summary>The host reported an unexpected bounded fault.</summary>
    HostFault = 7,
}

/// <summary>Contains the typed, bounded result of one participant lifecycle operation.</summary>
public readonly record struct RuntimeParticipantResultV1
{
    /// <summary>Initializes a validated lifecycle result.</summary>
    /// <param name="disposition">The closed transition disposition.</param>
    /// <param name="code">A bounded stable result code that consumers must escape before rendering.</param>
    /// <exception cref="ArgumentException"><paramref name="disposition"/> is outside the closed set or <paramref name="code"/> is invalid.</exception>
    public RuntimeParticipantResultV1(RuntimeParticipantDispositionV1 disposition, BoundedAscii code)
    {
        if (!Enum.IsDefined(disposition))
            throw new ArgumentException("The participant disposition is outside the closed registry.", nameof(disposition));
        if (!code.IsValid)
            throw new ArgumentException("A stable participant result code is required.", nameof(code));
        Disposition = disposition;
        Code = code;
    }

    /// <summary>Gets the closed lifecycle disposition.</summary>
    public RuntimeParticipantDispositionV1 Disposition { get; }

    /// <summary>Gets the bounded stable result code.</summary>
    public BoundedAscii Code { get; }

    /// <summary>Gets whether the transition completed successfully.</summary>
    public bool IsSuccess => Disposition == RuntimeParticipantDispositionV1.Succeeded;
}

/// <summary>Identifies one participant operation within an admitted session authority vector.</summary>
public readonly record struct RuntimeParticipantContextV1
{
    /// <summary>Initializes a validated participant context.</summary>
    /// <param name="participantId">The S1-allocated participant identity.</param>
    /// <param name="authority">The session and sparse owner-axis fences relevant to the participant.</param>
    /// <exception cref="ArgumentException"><paramref name="participantId"/> is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="authority"/> is null.</exception>
    public RuntimeParticipantContextV1(ParticipantId participantId, ExpectedAuthorityVectorV1 authority)
    {
        if (!participantId.IsValid)
            throw new ArgumentException("A participant identity is required.", nameof(participantId));
        ParticipantId = participantId;
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    /// <summary>Gets the S1-allocated participant identity.</summary>
    public ParticipantId ParticipantId { get; }

    /// <summary>Gets the session and sparse owner-axis fences.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
}

/// <summary>Describes one bounded, generation-fenced neutral runtime participant.</summary>
public sealed class RuntimeParticipantDescriptorV1
{
    /// <summary>The maximum number of dependencies or capacity dimensions on one participant.</summary>
    public const int MaximumRelatedItems = 32;

    private static readonly HashSet<string> RegisteredDimensions = AuthoritySchemaLedgerV1.Dimensions
        .Select(static row => row.Split('|')[1])
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Initializes a validated participant descriptor.</summary>
    /// <param name="id">The stable plan-local participant identifier.</param>
    /// <param name="owner">The qualified owner, such as S1 or S9.</param>
    /// <param name="seam">The neutral seam implemented by the participant.</param>
    /// <param name="dependencies">Plan-local participant identifiers that must start first.</param>
    /// <param name="generationFence">The registered owner axis fencing callbacks and effects.</param>
    /// <param name="maxPrepare">The positive preparation bound.</param>
    /// <param name="maxDrain">The positive drain bound.</param>
    /// <param name="maxTerminate">The positive termination bound.</param>
    /// <param name="capacityDimensions">Registered S2 dimension tokens charged by the participant.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dependencies"/> or <paramref name="capacityDimensions"/> is null.</exception>
    /// <exception cref="ArgumentException">A scalar, axis, duration, dependency, or dimension is invalid or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A related collection exceeds 32 items.</exception>
    public RuntimeParticipantDescriptorV1(
        BoundedAscii id,
        BoundedAscii owner,
        BoundedAscii seam,
        IEnumerable<BoundedAscii> dependencies,
        AuthorityAxisId generationFence,
        DurationNs maxPrepare,
        DurationNs maxDrain,
        DurationNs maxTerminate,
        IEnumerable<BoundedAscii> capacityDimensions)
    {
        if (!id.IsValid) throw new ArgumentException("A participant identifier is required.", nameof(id));
        if (!owner.IsValid) throw new ArgumentException("A participant owner is required.", nameof(owner));
        if (!seam.IsValid) throw new ArgumentException("A participant seam is required.", nameof(seam));
        if (!Enum.IsDefined(generationFence)) throw new ArgumentException("The generation fence is outside the closed axis registry.", nameof(generationFence));
        if (maxPrepare.Nanoseconds <= 0) throw new ArgumentException("The preparation bound must be positive.", nameof(maxPrepare));
        if (maxDrain.Nanoseconds <= 0) throw new ArgumentException("The drain bound must be positive.", nameof(maxDrain));
        if (maxTerminate.Nanoseconds <= 0) throw new ArgumentException("The termination bound must be positive.", nameof(maxTerminate));
        Id = id;
        Owner = owner;
        Seam = seam;
        Dependencies = Array.AsReadOnly(Canonicalize(dependencies, nameof(dependencies), null));
        GenerationFence = generationFence;
        MaxPrepare = maxPrepare;
        MaxDrain = maxDrain;
        MaxTerminate = maxTerminate;
        CapacityDimensions = Array.AsReadOnly(Canonicalize(capacityDimensions, nameof(capacityDimensions), RegisteredDimensions));
    }

    /// <summary>Gets the stable plan-local participant identifier.</summary>
    public BoundedAscii Id { get; }
    /// <summary>Gets the qualified participant owner.</summary>
    public BoundedAscii Owner { get; }
    /// <summary>Gets the neutral seam implemented by the participant.</summary>
    public BoundedAscii Seam { get; }
    /// <summary>Gets strictly ordered plan-local dependencies.</summary>
    public IReadOnlyList<BoundedAscii> Dependencies { get; }
    /// <summary>Gets the registered owner axis fencing callbacks and effects.</summary>
    public AuthorityAxisId GenerationFence { get; }
    /// <summary>Gets the positive preparation bound.</summary>
    public DurationNs MaxPrepare { get; }
    /// <summary>Gets the positive drain bound.</summary>
    public DurationNs MaxDrain { get; }
    /// <summary>Gets the positive termination bound.</summary>
    public DurationNs MaxTerminate { get; }
    /// <summary>Gets strictly ordered registered S2 capacity dimension tokens.</summary>
    public IReadOnlyList<BoundedAscii> CapacityDimensions { get; }

    private static BoundedAscii[] Canonicalize(IEnumerable<BoundedAscii> source, string parameterName, HashSet<string>? registry)
    {
        ArgumentNullException.ThrowIfNull(source, parameterName);
        var values = new List<BoundedAscii>();
        foreach (var value in source)
        {
            if (values.Count == MaximumRelatedItems)
                throw new ArgumentOutOfRangeException(parameterName, "A participant descriptor collection cannot exceed 32 items.");
            if (!value.IsValid || registry is not null && !registry.Contains(value.ToString()))
                throw new ArgumentException("A participant descriptor item is invalid or unregistered.", parameterName);
            values.Add(value);
        }
        values.Sort();
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index - 1] == values[index])
                throw new ArgumentException("A participant descriptor collection contains a duplicate.", parameterName);
        }
        return values.ToArray();
    }
}

/// <summary>Defines the owner-neutral lifecycle implemented by one runtime participant.</summary>
public interface IRuntimeParticipantV1 : IAsyncDisposable
{
    /// <summary>Gets the immutable participant descriptor.</summary>
    RuntimeParticipantDescriptorV1 Descriptor { get; }

    /// <summary>Prepares local resources without performing an external effect.</summary>
    /// <param name="context">The S1-allocated participant and authority fences.</param>
    /// <param name="cancellationToken">Cancels bounded preparation.</param>
    /// <returns>The typed preparation result.</returns>
    ValueTask<RuntimeParticipantResultV1> PrepareAsync(RuntimeParticipantContextV1 context, CancellationToken cancellationToken);

    /// <summary>Starts the already prepared participant after Agent admission.</summary>
    /// <param name="context">The same participant and authority fences used during preparation.</param>
    /// <param name="cancellationToken">Cancels bounded start.</param>
    /// <returns>The typed start result.</returns>
    ValueTask<RuntimeParticipantResultV1> StartAsync(RuntimeParticipantContextV1 context, CancellationToken cancellationToken);

    /// <summary>Stops new admission and converges already admitted work.</summary>
    /// <param name="intent">The graceful or forced drain intent.</param>
    /// <param name="cancellationToken">Cancels bounded drain.</param>
    /// <returns>The typed drain result.</returns>
    ValueTask<RuntimeParticipantResultV1> DrainAsync(RuntimeDrainIntentV1 intent, CancellationToken cancellationToken);

    /// <summary>Converges and releases owned operational resources within the termination bound.</summary>
    /// <param name="cause">The qualified termination cause.</param>
    /// <param name="cancellationToken">Cancels bounded termination.</param>
    /// <returns>The typed termination result.</returns>
    ValueTask<RuntimeParticipantResultV1> TerminateAsync(RuntimeTerminationCauseV1 cause, CancellationToken cancellationToken);
}
