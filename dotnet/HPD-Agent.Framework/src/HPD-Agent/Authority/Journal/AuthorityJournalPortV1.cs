namespace HPD.Agent.Authority;

/// <summary>Contains one immutable fact admitted at the sole S1.P0 journal linearization point.</summary>
public sealed class AuthorityFactEnvelopeV1
{
    /// <summary>The canonical envelope schema version.</summary>
    public const ushort SchemaVersion = 1;
    private readonly byte[] _payload;

    /// <summary>Initializes a structurally valid committed authority envelope and owns its payload bytes.</summary>
    /// <remarks>Construction alone confers no authority truth. An envelope names durable truth only when returned or read from the bound trusted journal after canonical validation, integrity creation, and atomic position assignment.</remarks>
    public AuthorityFactEnvelopeV1(
        JournalFactId factId,
        JournalPositionV1 position,
        ThreadPositionV1? threadScope,
        OwnerSliceId owner,
        SchemaReferenceV1 payloadSchema,
        ReadOnlySpan<byte> payload,
        Hash256 payloadHash,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt,
        UtcInstant admittedAt,
        IntegrityEnvelopeV1 integrity)
    {
        if (!factId.IsValid) throw new ArgumentException("A fact identity is required.", nameof(factId));
        if (!position.IsValid) throw new ArgumentException("A committed journal position is required.", nameof(position));
        if (threadScope is { IsValid: false }) throw new ArgumentException("A present thread position must be valid.", nameof(threadScope));
        if (!Enum.IsDefined(owner)) throw new ArgumentException("The owner is outside the closed registry.", nameof(owner));
        if (!payloadSchema.IsValid) throw new ArgumentException("A payload schema is required.", nameof(payloadSchema));
        if (payload.Length > ProposedAuthorityFactV1.MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        Span<byte> hashBytes = stackalloc byte[32];
        if (!payloadHash.TryWriteBytes(hashBytes)) throw new ArgumentException("A payload hash is required.", nameof(payloadHash));
        if (!correlation.IsValid) throw new ArgumentException("A correlation envelope is required.", nameof(correlation));
        ArgumentNullException.ThrowIfNull(integrity);
        FactId = factId;
        Position = position;
        ThreadScope = threadScope;
        Owner = owner;
        PayloadSchema = payloadSchema;
        _payload = payload.ToArray();
        Payload = Array.AsReadOnly(_payload);
        PayloadHash = payloadHash;
        Correlation = correlation;
        ObservedAt = observedAt;
        AdmittedAt = admittedAt;
        Integrity = integrity;
    }

    /// <summary>Gets the globally nonreusable fact identity.</summary>
    public JournalFactId FactId { get; }
    /// <summary>Gets the sole session journal position.</summary>
    public JournalPositionV1 Position { get; }
    /// <summary>Gets the optional secondary thread position.</summary>
    public ThreadPositionV1? ThreadScope { get; }
    /// <summary>Gets the semantic owner of the payload schema.</summary>
    public OwnerSliceId Owner { get; }
    /// <summary>Gets the exact payload schema version.</summary>
    public SchemaReferenceV1 PayloadSchema { get; }
    /// <summary>Gets a read-only view of the owned canonical payload.</summary>
    public IReadOnlyList<byte> Payload { get; }
    /// <summary>Gets the schema-bound canonical payload hash.</summary>
    public Hash256 PayloadHash { get; }
    /// <summary>Gets bounded nonordering correlations.</summary>
    public CorrelationEnvelopeV1 Correlation { get; }
    /// <summary>Gets the producer audit timestamp.</summary>
    public UtcInstant ObservedAt { get; }
    /// <summary>Gets the journal admission timestamp.</summary>
    public UtcInstant AdmittedAt { get; }
    /// <summary>Gets the stored canonical integrity evidence.</summary>
    public IntegrityEnvelopeV1 Integrity { get; }
    internal ReadOnlySpan<byte> PayloadBytes => _payload;
}

/// <summary>Represents every closed outcome of one authority append attempt.</summary>
/// <remarks>Constructing a result is structural only. Durable truth is named only when a result is returned by the bound trusted journal.</remarks>
public abstract record AppendAuthorityResultV1
{
    private AppendAuthorityResultV1() { }

    /// <summary>Reports an atomically committed batch.</summary>
    public sealed record Committed : AppendAuthorityResultV1
    {
        /// <summary>Initializes a committed result.</summary>
        public Committed(long previousHead, long currentHead, IEnumerable<AuthorityFactEnvelopeV1> envelopes)
        {
            Envelopes = OwnEnvelopes(envelopes);
            if (previousHead < 0 || currentHead != checked(previousHead + Envelopes.Count)) throw new ArgumentOutOfRangeException(nameof(currentHead));
            var session = Envelopes[0].Position.Session;
            for (var index = 0; index < Envelopes.Count; index++)
            {
                var position = Envelopes[index].Position;
                if (position.Session != session || position.Sequence != previousHead + index + 1)
                    throw new ArgumentException("Committed envelopes must occupy one contiguous session range.", nameof(envelopes));
            }
            PreviousHead = previousHead;
            CurrentHead = currentHead;
        }
        /// <summary>Gets the head before the atomic append.</summary>
        public long PreviousHead { get; }
        /// <summary>Gets the head after the atomic append.</summary>
        public long CurrentHead { get; }
        /// <summary>Gets the committed envelopes in journal order.</summary>
        public IReadOnlyList<AuthorityFactEnvelopeV1> Envelopes { get; }
    }
    /// <summary>Reports that every proposal was already committed with the same complete identity.</summary>
    public sealed record AlreadyCommitted : AppendAuthorityResultV1
    {
        /// <summary>Initializes an idempotent committed result.</summary>
        public AlreadyCommitted(IEnumerable<AuthorityFactEnvelopeV1> envelopes) => Envelopes = OwnEnvelopes(envelopes);
        /// <summary>Gets the original committed envelopes in journal order.</summary>
        public IReadOnlyList<AuthorityFactEnvelopeV1> Envelopes { get; }
    }
    /// <summary>Reports a failed expected-session-head comparison.</summary>
    public sealed record SessionConflict : AppendAuthorityResultV1
    {
        /// <summary>Initializes a session-head conflict.</summary>
        public SessionConflict(long expected, long actual)
        { if (expected < 0 || actual < 0) throw new ArgumentOutOfRangeException(nameof(expected)); Expected = expected; Actual = actual; }
        /// <summary>Gets the requested expected head.</summary>
        public long Expected { get; }
        /// <summary>Gets the observed current head.</summary>
        public long Actual { get; }
    }
    /// <summary>Reports a failed expected-thread-head comparison.</summary>
    public sealed record ThreadConflict : AppendAuthorityResultV1
    {
        /// <summary>Initializes a thread-head conflict.</summary>
        public ThreadConflict(ThreadId threadId, long expected, long actual)
        {
            if (!threadId.IsValid) throw new ArgumentException("A thread identity is required.", nameof(threadId));
            if (expected < 0 || actual < 0) throw new ArgumentOutOfRangeException(nameof(expected));
            ThreadId = threadId; Expected = expected; Actual = actual;
        }
        /// <summary>Gets the conflicting thread.</summary>
        public ThreadId ThreadId { get; }
        /// <summary>Gets the requested expected head.</summary>
        public long Expected { get; }
        /// <summary>Gets the observed current head.</summary>
        public long Actual { get; }
    }
    /// <summary>Reports reuse of a fact identity with a contradictory complete identity.</summary>
    public sealed record ContradictoryDuplicate : AppendAuthorityResultV1
    {
        /// <summary>Initializes a contradictory duplicate result.</summary>
        public ContradictoryDuplicate(JournalFactId factId, Hash256 originalHash, Hash256 proposedHash)
        {
            Span<byte> bytes = stackalloc byte[32];
            if (!factId.IsValid || !originalHash.TryWriteBytes(bytes) || !proposedHash.TryWriteBytes(bytes)) throw new ArgumentException("Valid fact and hash identities are required.");
            FactId = factId; OriginalHash = originalHash; ProposedHash = proposedHash;
        }
        /// <summary>Gets the reused fact identity.</summary>
        public JournalFactId FactId { get; }
        /// <summary>Gets the original committed hash.</summary>
        public Hash256 OriginalHash { get; }
        /// <summary>Gets the proposed contradictory hash.</summary>
        public Hash256 ProposedHash { get; }
    }
    /// <summary>Reports an unregistered payload schema.</summary>
    public sealed record UnknownSchema : AppendAuthorityResultV1
    {
        /// <summary>Initializes an unknown-schema result.</summary>
        public UnknownSchema(SchemaReferenceV1 schema) => Schema = schema.IsValid ? schema : throw new ArgumentException("A schema reference is required.", nameof(schema));
        /// <summary>Gets the unregistered schema reference.</summary>
        public SchemaReferenceV1 Schema { get; }
    }
    /// <summary>Reports a canonical payload validation failure without exposing payload data.</summary>
    public sealed record InvalidPayload : AppendAuthorityResultV1
    {
        /// <summary>Initializes an invalid-payload result.</summary>
        public InvalidPayload(BoundedAscii safeCode) => SafeCode = safeCode.IsValid ? safeCode : throw new ArgumentException("A safe code is required.", nameof(safeCode));
        /// <summary>Gets the bounded nonsecret failure code.</summary>
        public BoundedAscii SafeCode { get; }
    }
    /// <summary>Reports a capacity refusal before P0.</summary>
    public sealed record CapacityRefused : AppendAuthorityResultV1
    {
        /// <summary>Initializes a capacity refusal.</summary>
        public CapacityRefused(CapacityDimensionId dimension, ulong required, ulong available)
        {
            if (!Enum.IsDefined(dimension)) throw new ArgumentException("A registered capacity dimension is required.", nameof(dimension));
            if (required == 0 || required <= available) throw new ArgumentOutOfRangeException(nameof(required));
            Dimension = dimension; Required = required; Available = available;
        }
        /// <summary>Gets the registered capacity dimension token.</summary>
        public CapacityDimensionId Dimension { get; }
        /// <summary>Gets the required units.</summary>
        public ulong Required { get; }
        /// <summary>Gets the available units.</summary>
        public ulong Available { get; }
    }
    /// <summary>Reports a store failure that proves neither commit nor noncommit.</summary>
    public sealed record StoreUnavailable : AppendAuthorityResultV1
    {
        /// <summary>Initializes a store-unavailable result.</summary>
        public StoreUnavailable(BoundedAscii safeCode) => SafeCode = safeCode.IsValid ? safeCode : throw new ArgumentException("A safe code is required.", nameof(safeCode));
        /// <summary>Gets the bounded nonsecret failure code.</summary>
        public BoundedAscii SafeCode { get; }
    }
    /// <summary>Reports an ambiguous outcome that must be reconciled by fact identity or prefix verification.</summary>
    public sealed record OutcomeUnknown : AppendAuthorityResultV1
    {
        /// <summary>Initializes an ambiguous outcome.</summary>
        public OutcomeUnknown(OperationId operationId) => OperationId = operationId.IsValid ? operationId : throw new ArgumentException("An operation identity is required.", nameof(operationId));
        /// <summary>Gets the operation identity used for reconciliation.</summary>
        public OperationId OperationId { get; }
    }

    private static IReadOnlyList<AuthorityFactEnvelopeV1> OwnEnvelopes(IEnumerable<AuthorityFactEnvelopeV1> envelopes)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        var owned = new List<AuthorityFactEnvelopeV1>();
        foreach (var envelope in envelopes)
        {
            if (owned.Count == AppendAuthorityBatchV1.MaximumItems) throw new ArgumentOutOfRangeException(nameof(envelopes));
            if (envelope is null) throw new ArgumentException("An envelope cannot be null.", nameof(envelopes));
            owned.Add(envelope);
        }
        if (owned.Count == 0) throw new ArgumentOutOfRangeException(nameof(envelopes));
        return Array.AsReadOnly(owned.ToArray());
    }
}

/// <summary>Identifies one registered S2 capacity dimension.</summary>
public enum CapacityDimensionId : ushort
{
    /// <summary>Resident raw media bytes.</summary>
    MediaBytes = 1,
    /// <summary>Resident encoded bytes.</summary>
    EncodedBytes = 2,
    /// <summary>Resident queue items.</summary>
    QueueItems = 3,
    /// <summary>Resident audio samples.</summary>
    AudioSamples = 4,
    /// <summary>Resident buffered nanoseconds.</summary>
    BufferNanoseconds = 5,
    /// <summary>Exclusive provider operations.</summary>
    ProviderInflight = 6,
    /// <summary>Exclusive output operations.</summary>
    OutputInflight = 7,
    /// <summary>Resident subscriber items.</summary>
    SubscriberItems = 8,
    /// <summary>Resident subscriber bytes.</summary>
    SubscriberBytes = 9,
    /// <summary>Resident journal bytes.</summary>
    JournalBytes = 10,
    /// <summary>Resident copy obligations.</summary>
    CopyObligations = 11,
    /// <summary>Resident quarantine bytes.</summary>
    QuarantineBytes = 12,
    /// <summary>Diagnostic cardinality inside a rate window.</summary>
    DiagnosticCardinality = 13,
    /// <summary>Resident recovery work items.</summary>
    RecoveryWork = 14,
}

/// <summary>Defines the sole neutral append/CAS admission port for authority facts.</summary>
public interface IAuthorityJournalV1
{
    /// <summary>Validates and atomically admits a complete batch or returns one closed noncommitting outcome.</summary>
    /// <param name="request">The structurally bounded proposal batch.</param>
    /// <param name="cancellationToken">Requests cancellation without proving whether P0 occurred.</param>
    /// <returns>One closed append disposition.</returns>
    /// <remarks>The trusted implementation revalidates schema, version, owner, canonical payload, schema-bound hash, exact canonical size, fact identity, session head, and thread heads before mutation. Cancellation and exceptions prove neither commit nor noncommit. Callers must reconcile ambiguous outcomes before retrying a different payload under a fact identity.</remarks>
    ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default);
}
