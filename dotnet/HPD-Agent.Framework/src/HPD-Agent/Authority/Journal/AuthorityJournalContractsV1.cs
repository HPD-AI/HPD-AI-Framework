namespace HPD.Agent.Authority;

/// <summary>Identifies the sole semantic owner of an authority payload schema.</summary>
public enum OwnerSliceId : ushort
{
    /// <summary>Session lifecycle and authority admission.</summary>
    S1 = 1,
    /// <summary>Media graph and resource authority.</summary>
    S2 = 2,
    /// <summary>Activity evidence.</summary>
    S3 = 3,
    /// <summary>Turn decisions and finality.</summary>
    S4 = 4,
    /// <summary>Provider protocol effects.</summary>
    S5 = 5,
    /// <summary>Output and heard-state authority.</summary>
    S6 = 6,
    /// <summary>Interruption and tool transactions.</summary>
    S7 = 7,
    /// <summary>Route compilation and cutover.</summary>
    S8 = 8,
    /// <summary>History, privacy, copies, and retention.</summary>
    S9 = 9,
    /// <summary>Replay and deterministic oracle evidence.</summary>
    S10 = 10,
    /// <summary>Concrete transport effects.</summary>
    S11 = 11,
    /// <summary>Agent semantic acceptance distinct from an Audio protocol namespace.</summary>
    AgentCore = 12,
}

/// <summary>Contains bounded correlations that never define authority ordering.</summary>
public readonly record struct CorrelationEnvelopeV1
{
    /// <summary>Initializes a validated correlation envelope.</summary>
    /// <param name="tenantId">The required tenant boundary.</param>
    /// <param name="principalId">An optional authenticated principal correlation.</param>
    /// <param name="sessionId">An optional durable Agent session correlation.</param>
    /// <param name="threadId">An optional durable thread correlation.</param>
    /// <param name="participantId">An optional participant correlation.</param>
    /// <param name="operationId">An optional operation correlation.</param>
    /// <exception cref="ArgumentException">A required or present identity is invalid.</exception>
    public CorrelationEnvelopeV1(
        TenantId tenantId,
        PrincipalId? principalId = null,
        SessionId? sessionId = null,
        ThreadId? threadId = null,
        ParticipantId? participantId = null,
        OperationId? operationId = null)
    {
        if (!tenantId.IsValid) throw new ArgumentException("A tenant identity is required.", nameof(tenantId));
        if (principalId is { IsValid: false }) throw new ArgumentException("A present principal identity must be valid.", nameof(principalId));
        if (sessionId is { IsValid: false }) throw new ArgumentException("A present session identity must be valid.", nameof(sessionId));
        if (threadId is { IsValid: false }) throw new ArgumentException("A present thread identity must be valid.", nameof(threadId));
        if (participantId is { IsValid: false }) throw new ArgumentException("A present participant identity must be valid.", nameof(participantId));
        if (operationId is { IsValid: false }) throw new ArgumentException("A present operation identity must be valid.", nameof(operationId));
        TenantId = tenantId;
        PrincipalId = principalId;
        SessionId = sessionId;
        ThreadId = threadId;
        ParticipantId = participantId;
        OperationId = operationId;
    }

    /// <summary>Gets the required tenant boundary.</summary>
    public TenantId TenantId { get; }
    /// <summary>Gets the optional authenticated principal correlation.</summary>
    public PrincipalId? PrincipalId { get; }
    /// <summary>Gets the optional durable Agent session correlation.</summary>
    public SessionId? SessionId { get; }
    /// <summary>Gets the optional durable thread correlation.</summary>
    public ThreadId? ThreadId { get; }
    /// <summary>Gets the optional participant correlation.</summary>
    public ParticipantId? ParticipantId { get; }
    /// <summary>Gets the optional operation correlation.</summary>
    public OperationId? OperationId { get; }
    /// <summary>Gets whether every required or present identity is valid.</summary>
    public bool IsValid => TenantId.IsValid && PrincipalId is not { IsValid: false } &&
        SessionId is not { IsValid: false } && ThreadId is not { IsValid: false } &&
        ParticipantId is not { IsValid: false } && OperationId is not { IsValid: false };
}

/// <summary>Contains one immutable, bounded authority fact proposed before trusted admission validation and position assignment.</summary>
/// <remarks>A proposal is not authority truth. The journal revalidates schema, version, owner, canonical bytes, and hash before P0.</remarks>
public sealed class ProposedAuthorityFactV1
{
    /// <summary>The maximum canonical payload size in bytes.</summary>
    public const int MaximumPayloadBytes = 1_048_576;
    private readonly byte[] _payload;

    /// <summary>Initializes a validated proposed authority fact and owns its payload bytes.</summary>
    /// <param name="factId">The globally nonreusable fact identity.</param>
    /// <param name="threadId">The optional thread receiving a secondary position.</param>
    /// <param name="owner">The registered semantic owner.</param>
    /// <param name="payloadSchema">The exact registered payload schema version.</param>
    /// <param name="payload">Canonical owner-payload bytes.</param>
    /// <param name="payloadHash">The schema-bound canonical payload hash.</param>
    /// <param name="correlation">Bounded nonordering correlations.</param>
    /// <param name="observedAt">The producer's audit timestamp; never an ordering source.</param>
    /// <exception cref="ArgumentException">An identity, enum, schema, hash, correlation, or timestamp is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="payload"/> exceeds one MiB.</exception>
    public ProposedAuthorityFactV1(
        JournalFactId factId,
        ThreadId? threadId,
        OwnerSliceId owner,
        SchemaReferenceV1 payloadSchema,
        ReadOnlySpan<byte> payload,
        Hash256 payloadHash,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt)
    {
        if (!factId.IsValid) throw new ArgumentException("A fact identity is required.", nameof(factId));
        if (threadId is { } thread && !thread.IsValid) throw new ArgumentException("A present thread identity must be valid.", nameof(threadId));
        if (!Enum.IsDefined(owner)) throw new ArgumentException("The owner is outside the closed registry.", nameof(owner));
        if (!payloadSchema.IsValid) throw new ArgumentException("A payload schema is required.", nameof(payloadSchema));
        if (payload.Length > MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload), "An authority payload cannot exceed one MiB.");
        Span<byte> hashBytes = stackalloc byte[32];
        if (!payloadHash.TryWriteBytes(hashBytes)) throw new ArgumentException("A canonical payload hash is required.", nameof(payloadHash));
        if (!correlation.IsValid) throw new ArgumentException("A valid correlation envelope is required.", nameof(correlation));
        FactId = factId;
        ThreadId = threadId;
        Owner = owner;
        PayloadSchema = payloadSchema;
        _payload = payload.ToArray();
        Payload = Array.AsReadOnly(_payload);
        PayloadHash = payloadHash;
        Correlation = correlation;
        ObservedAt = observedAt;
    }

    /// <summary>Gets the globally nonreusable fact identity.</summary>
    public JournalFactId FactId { get; }
    /// <summary>Gets the optional thread receiving a secondary position.</summary>
    public ThreadId? ThreadId { get; }
    /// <summary>Gets the registered semantic owner.</summary>
    public OwnerSliceId Owner { get; }
    /// <summary>Gets the exact payload schema version.</summary>
    public SchemaReferenceV1 PayloadSchema { get; }
    /// <summary>Gets a read-only view of the owned canonical payload bytes.</summary>
    public IReadOnlyList<byte> Payload { get; }
    /// <summary>Gets the schema-bound canonical payload hash.</summary>
    public Hash256 PayloadHash { get; }
    /// <summary>Gets bounded nonordering correlations.</summary>
    public CorrelationEnvelopeV1 Correlation { get; }
    /// <summary>Gets the producer audit timestamp.</summary>
    public UtcInstant ObservedAt { get; }
    internal ReadOnlySpan<byte> PayloadBytes => _payload;
}

/// <summary>Pins the expected secondary head of one thread generation.</summary>
public readonly record struct ThreadExpectedHeadV1
{
    /// <summary>Initializes a validated expected thread head.</summary>
    /// <param name="threadId">The thread identity.</param>
    /// <param name="generation">The positive thread generation.</param>
    /// <param name="sequence">The nonnegative expected sequence; zero means before the first fact.</param>
    /// <exception cref="ArgumentException"><paramref name="threadId"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Generation is not positive or sequence is negative.</exception>
    public ThreadExpectedHeadV1(ThreadId threadId, long generation, long sequence)
    {
        if (!threadId.IsValid) throw new ArgumentException("A thread identity is required.", nameof(threadId));
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation), "A thread generation must be positive.");
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence), "An expected thread sequence cannot be negative.");
        ThreadId = threadId;
        Generation = generation;
        Sequence = sequence;
    }

    /// <summary>Gets the thread identity.</summary>
    public ThreadId ThreadId { get; }
    /// <summary>Gets the positive thread generation.</summary>
    public long Generation { get; }
    /// <summary>Gets the nonnegative expected sequence.</summary>
    public long Sequence { get; }
    /// <summary>Gets whether the expected head is valid.</summary>
    public bool IsValid => ThreadId.IsValid && Generation > 0 && Sequence >= 0;
}

/// <summary>Contains one bounded atomic append/CAS request.</summary>
public sealed class AppendAuthorityBatchV1
{
    /// <summary>The maximum number of facts or thread expectations in one batch.</summary>
    public const int MaximumItems = 256;
    private readonly ProposedAuthorityFactV1[] _facts;

    /// <summary>Initializes and validates a complete append batch before mutation.</summary>
    /// <param name="session">The authority session key.</param>
    /// <param name="expectedSessionHead">The nonnegative expected session head.</param>
    /// <param name="expectedThreadHeads">Strictly unique expected thread-generation heads.</param>
    /// <param name="facts">One to 256 proposed facts in caller-significant order.</param>
    /// <param name="maximumEncodedBytes">The positive bounded encoded-byte admission limit.</param>
    /// <exception cref="ArgumentNullException">A collection is null.</exception>
    /// <exception cref="ArgumentException">A scalar, item, identity, or scope is invalid or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count or byte bound is invalid.</exception>
    public AppendAuthorityBatchV1(
        SessionAuthorityStampV1 session,
        long expectedSessionHead,
        IEnumerable<ThreadExpectedHeadV1> expectedThreadHeads,
        IEnumerable<ProposedAuthorityFactV1> facts,
        uint maximumEncodedBytes)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session authority key is required.", nameof(session));
        if (expectedSessionHead < 0) throw new ArgumentOutOfRangeException(nameof(expectedSessionHead));
        if (maximumEncodedBytes == 0 || maximumEncodedBytes > ProposedAuthorityFactV1.MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes), "The encoded-byte limit must be between one and one MiB.");
        ArgumentNullException.ThrowIfNull(expectedThreadHeads);
        ArgumentNullException.ThrowIfNull(facts);

        var heads = new List<ThreadExpectedHeadV1>();
        foreach (var head in expectedThreadHeads)
        {
            if (heads.Count == MaximumItems) throw new ArgumentOutOfRangeException(nameof(expectedThreadHeads));
            if (!head.IsValid) throw new ArgumentException("An expected thread head is invalid.", nameof(expectedThreadHeads));
            heads.Add(head);
        }
        heads.Sort(static (left, right) => CompareThreadIds(left.ThreadId, right.ThreadId));
        for (var index = 1; index < heads.Count; index++)
            if (heads[index - 1].ThreadId == heads[index].ThreadId)
                throw new ArgumentException("Expected thread identities must be unique.", nameof(expectedThreadHeads));

        var proposed = new List<ProposedAuthorityFactV1>();
        var factIds = new HashSet<JournalFactId>();
        foreach (var fact in facts)
        {
            if (proposed.Count == MaximumItems) throw new ArgumentOutOfRangeException(nameof(facts));
            if (fact is null) throw new ArgumentException("A proposed fact cannot be null.", nameof(facts));
            if (!factIds.Add(fact.FactId)) throw new ArgumentException("A batch cannot repeat a fact identity.", nameof(facts));
            proposed.Add(fact);
        }
        if (proposed.Count == 0) throw new ArgumentOutOfRangeException(nameof(facts), "An append batch requires at least one fact.");
        var scopedThreads = proposed.Where(static fact => fact.ThreadId.HasValue)
            .Select(static fact => fact.ThreadId!.Value).Distinct().ToArray();
        if (scopedThreads.Length != heads.Count || scopedThreads.Any(thread => !heads.Any(head => head.ThreadId == thread)))
            throw new ArgumentException("Expected thread heads must exactly cover the batch's thread-scoped facts.", nameof(expectedThreadHeads));


        Session = session;
        ExpectedSessionHead = expectedSessionHead;
        ExpectedThreadHeads = Array.AsReadOnly(heads.ToArray());
        _facts = proposed.ToArray();
        Facts = Array.AsReadOnly(_facts);
        MaximumEncodedBytes = maximumEncodedBytes;
    }

    /// <summary>Gets the authority session key.</summary>
    public SessionAuthorityStampV1 Session { get; }
    /// <summary>Gets the nonnegative expected session head.</summary>
    public long ExpectedSessionHead { get; }
    /// <summary>Gets expected thread heads in canonical thread-ID order.</summary>
    public IReadOnlyList<ThreadExpectedHeadV1> ExpectedThreadHeads { get; }
    /// <summary>Gets proposed facts in caller-significant atomic order.</summary>
    public IReadOnlyList<ProposedAuthorityFactV1> Facts { get; }
    /// <summary>Gets the caller's positive encoded-byte admission limit; the trusted journal computes and enforces the exact canonical size.</summary>
    public uint MaximumEncodedBytes { get; }

    private static int CompareThreadIds(ThreadId left, ThreadId right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        left.TryWriteBytes(leftBytes);
        right.TryWriteBytes(rightBytes);
        return leftBytes.SequenceCompareTo(rightBytes);
    }
}
