using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Identifies the closed stable-identity family carried by a capacity subject.</summary>
public enum CapacitySubjectKindV1 : ushort
{
    /// <summary>A tenant identity.</summary>
    Tenant = 1,
    /// <summary>A durable session identity.</summary>
    Session = 2,
    /// <summary>A live participant identity.</summary>
    Participant = 3,
    /// <summary>An operation identity.</summary>
    Operation = 4,
    /// <summary>A provider identity.</summary>
    Provider = 5,
    /// <summary>A custodian descriptor identity.</summary>
    Custodian = 6,
    /// <summary>An exporter identity.</summary>
    Exporter = 7,
    /// <summary>A subscriber identity.</summary>
    Subscriber = 8,
    /// <summary>A registered schema identity.</summary>
    Schema = 9,
    /// <summary>A closed owner-slice value.</summary>
    Owner = 10,
    /// <summary>An output sink generation identity.</summary>
    Sink = 11,
}

/// <summary>Identifies the closed canonical wire arm used by a capacity subject value.</summary>
public enum CapacitySubjectValueKindV1 : ushort
{
    /// <summary>The value is a nonzero 16-byte identity from the subject kind's registered family.</summary>
    StableId = 1,
    /// <summary>The value is a registered <see cref="OwnerSliceId"/>.</summary>
    OwnerSlice = 2,
}

/// <summary>Contains one closed kind-bound identity used as a capacity balance key.</summary>
public abstract record CapacitySubjectV1
{
    private CapacitySubjectV1() { }

    /// <summary>Gets the closed subject kind.</summary>
    public abstract CapacitySubjectKindV1 Kind { get; }

    internal abstract bool TryWriteIdentity(Span<byte> destination, out int bytesWritten);

    /// <summary>Contains a non-default tenant identity.</summary>
    public sealed record Tenant : CapacitySubjectV1
    {
        /// <summary>Initializes a tenant subject.</summary>
        /// <param name="value">The non-default tenant identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Tenant(TenantId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the tenant identity.</summary>
        public TenantId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Tenant;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default durable session identity.</summary>
    public sealed record Session : CapacitySubjectV1
    {
        /// <summary>Initializes a session subject.</summary>
        /// <param name="value">The non-default session identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Session(SessionId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the durable session identity.</summary>
        public SessionId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Session;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default participant identity.</summary>
    public sealed record Participant : CapacitySubjectV1
    {
        /// <summary>Initializes a participant subject.</summary>
        /// <param name="value">The non-default participant identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Participant(ParticipantId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the participant identity.</summary>
        public ParticipantId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Participant;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default operation identity.</summary>
    public sealed record Operation : CapacitySubjectV1
    {
        /// <summary>Initializes an operation subject.</summary>
        /// <param name="value">The non-default operation identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Operation(OperationId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the operation identity.</summary>
        public OperationId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Operation;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default provider identity.</summary>
    public sealed record Provider : CapacitySubjectV1
    {
        /// <summary>Initializes a provider subject.</summary>
        /// <param name="value">The non-default provider identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Provider(ProviderId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the provider identity.</summary>
        public ProviderId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Provider;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default custodian descriptor identity.</summary>
    public sealed record Custodian : CapacitySubjectV1
    {
        /// <summary>Initializes a custodian subject.</summary>
        /// <param name="value">The non-default custodian descriptor identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Custodian(CustodianDescriptorId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the custodian descriptor identity.</summary>
        public CustodianDescriptorId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Custodian;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default exporter identity.</summary>
    public sealed record Exporter : CapacitySubjectV1
    {
        /// <summary>Initializes an exporter subject.</summary>
        /// <param name="value">The non-default exporter identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Exporter(ExportId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the exporter identity.</summary>
        public ExportId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Exporter;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default subscriber identity.</summary>
    public sealed record Subscriber : CapacitySubjectV1
    {
        /// <summary>Initializes a subscriber subject.</summary>
        /// <param name="value">The non-default subscriber identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Subscriber(SubscriberId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the subscriber identity.</summary>
        public SubscriberId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Subscriber;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains a non-default schema identity.</summary>
    public sealed record Schema : CapacitySubjectV1
    {
        /// <summary>Initializes a schema subject.</summary>
        /// <param name="value">The non-default schema identity.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Schema(SchemaId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the schema identity.</summary>
        public SchemaId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Schema;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    /// <summary>Contains one registered owner-slice value without fabricating a stable identity.</summary>
    public sealed record Owner : CapacitySubjectV1
    {
        /// <summary>Initializes an owner subject.</summary>
        /// <param name="value">The registered owner slice.</param>
        /// <exception cref="ArgumentException">The owner is not registered.</exception>
        public Owner(OwnerSliceId value) => Value = Enum.IsDefined(value) ? value : throw new ArgumentException("A registered owner slice is required.", nameof(value));
        /// <summary>Gets the registered owner slice.</summary>
        public OwnerSliceId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Owner;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < 2) { bytesWritten = 0; return false; }
            global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)Value);
            bytesWritten = 2;
            return true;
        }
    }

    /// <summary>Contains a non-default sink-generation identity.</summary>
    public sealed record Sink : CapacitySubjectV1
    {
        /// <summary>Initializes a sink subject.</summary>
        /// <param name="value">The non-default sink generation.</param>
        /// <exception cref="ArgumentException">The identity is the invalid default.</exception>
        public Sink(SinkGenerationId value) => Value = Require(value.IsValid, value, nameof(value));
        /// <summary>Gets the sink-generation identity.</summary>
        public SinkGenerationId Value { get; }
        /// <inheritdoc />
        public override CapacitySubjectKindV1 Kind => CapacitySubjectKindV1.Sink;
        internal override bool TryWriteIdentity(Span<byte> destination, out int bytesWritten) => Write(Value.TryWriteBytes(destination), out bytesWritten);
    }

    private static T Require<T>(bool valid, T value, string parameterName) => valid ? value : throw new ArgumentException("A non-default registered identity is required.", parameterName);
    private static bool Write(bool success, out int bytesWritten) { bytesWritten = success ? 16 : 0; return success; }
}

/// <summary>Contains the canonical authorization context and balance identity for one capacity charge.</summary>
public sealed record CapacityScopeV1
{
    /// <summary>Initializes a canonical capacity scope.</summary>
    /// <param name="tenantId">The required tenant identity.</param>
    /// <param name="sessionId">The optional durable session correlation.</param>
    /// <param name="subject">The optional typed subject.</param>
    /// <exception cref="ArgumentException">An identity is default or the tenant/session/subject shape is noncanonical.</exception>
    public CapacityScopeV1(TenantId tenantId, SessionId? sessionId = null, CapacitySubjectV1? subject = null)
    {
        if (!tenantId.IsValid) throw new ArgumentException("A tenant identity is required.", nameof(tenantId));
        if (sessionId is { IsValid: false }) throw new ArgumentException("A non-default session identity is required when present.", nameof(sessionId));
        if (subject is CapacitySubjectV1.Tenant or CapacitySubjectV1.Session)
            throw new ArgumentException("Tenant and session scopes use their dedicated fields, not a subject value.", nameof(subject));
        TenantId = tenantId;
        SessionId = sessionId;
        Subject = subject;
        Kind = subject?.Kind switch
        {
            null when sessionId is null => CapacityScopeKindV1.Tenant,
            null => CapacityScopeKindV1.Session,
            CapacitySubjectKindV1.Participant => CapacityScopeKindV1.Participant,
            CapacitySubjectKindV1.Operation => CapacityScopeKindV1.Operation,
            CapacitySubjectKindV1.Provider => CapacityScopeKindV1.Provider,
            CapacitySubjectKindV1.Custodian => CapacityScopeKindV1.Custodian,
            CapacitySubjectKindV1.Exporter => CapacityScopeKindV1.Exporter,
            CapacitySubjectKindV1.Subscriber => CapacityScopeKindV1.Subscriber,
            CapacitySubjectKindV1.Schema => CapacityScopeKindV1.Schema,
            CapacitySubjectKindV1.Owner => CapacityScopeKindV1.Owner,
            CapacitySubjectKindV1.Sink => CapacityScopeKindV1.Sink,
            _ => throw new ArgumentException("The subject kind is not registered for a scope.", nameof(subject)),
        };
    }

    /// <summary>Gets the canonical scope kind derived from the present identity.</summary>
    public CapacityScopeKindV1 Kind { get; }
    /// <summary>Gets the required tenant authorization context.</summary>
    public TenantId TenantId { get; }
    /// <summary>Gets the optional durable session correlation.</summary>
    public SessionId? SessionId { get; }
    /// <summary>Gets the typed subject for non-tenant and non-session scopes.</summary>
    public CapacitySubjectV1? Subject { get; }
}

/// <summary>Identifies the admission class of one atomic capacity request.</summary>
public enum CapacityPriorityV1 : ushort
{
    /// <summary>Ordinary work that cannot consume an emergency reserve.</summary>
    Normal = 1,
    /// <summary>Bounded control-plane work that cannot consume an emergency reserve.</summary>
    Control = 2,
    /// <summary>Mandatory authority work eligible only for a matching authority reserve.</summary>
    Authority = 3,
    /// <summary>Mandatory privacy work eligible only for a matching privacy reserve.</summary>
    Privacy = 4,
    /// <summary>Bounded recovery work eligible only for a matching recovery reserve.</summary>
    Recovery = 5,
}

/// <summary>Contains one positive, dimension-bounded capacity charge.</summary>
public sealed record CapacityChargeV1
{
    /// <summary>Initializes one validated capacity charge.</summary>
    /// <param name="dimensionId">The registered dimension.</param>
    /// <param name="scope">The canonical typed scope.</param>
    /// <param name="amount">The positive amount in the descriptor's unit.</param>
    /// <param name="purpose">The registered S2 purpose identity.</param>
    /// <exception cref="ArgumentException">A scope or purpose is invalid, or the dimension does not permit the scope kind.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The amount is outside the registered per-charge bound.</exception>
    public CapacityChargeV1(CapacityDimensionId dimensionId, CapacityScopeV1 scope, long amount, CapacityPurposeId purpose)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!purpose.IsValid) throw new ArgumentException("A capacity purpose is required.", nameof(purpose));
        var descriptor = CapacityDimensionRegistryV1.Get(dimensionId);
        if (!descriptor.ScopeKinds.Contains(scope.Kind)) throw new ArgumentException("The scope kind is not registered for the dimension.", nameof(scope));
        if (amount <= 0 || amount > descriptor.MaximumPerCharge) throw new ArgumentOutOfRangeException(nameof(amount));
        DimensionId = dimensionId;
        Scope = scope;
        Amount = amount;
        Purpose = purpose;
    }

    /// <summary>Gets the registered dimension.</summary>
    public CapacityDimensionId DimensionId { get; }
    /// <summary>Gets the canonical typed scope.</summary>
    public CapacityScopeV1 Scope { get; }
    /// <summary>Gets the positive amount in the registered unit.</summary>
    public long Amount { get; }
    /// <summary>Gets the registered purpose.</summary>
    public CapacityPurposeId Purpose { get; }
}

/// <summary>Contains one deeply owned, atomic S2 capacity reservation request.</summary>
public sealed record CapacityRequestV1
{
    /// <summary>The maximum number of distinct charges in one request.</summary>
    public const int MaximumCharges = 256;

    /// <summary>Initializes one immutable capacity request.</summary>
    /// <param name="operationId">The stable idempotency identity.</param>
    /// <param name="authority">The sparse expected authority vector.</param>
    /// <param name="charges">One to 256 distinct charges.</param>
    /// <param name="deadline">The absolute monotonic deadline.</param>
    /// <param name="priority">The closed admission class.</param>
    /// <exception cref="ArgumentException">An identity, vector, deadline, priority or charge is invalid or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The charge count is outside 1..256.</exception>
    public CapacityRequestV1(OperationId operationId, ExpectedAuthorityVectorV1 authority, IEnumerable<CapacityChargeV1> charges, MonotonicStampV1 deadline, CapacityPriorityV1 priority)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        ArgumentNullException.ThrowIfNull(authority);
        if (!deadline.IsValid) throw new ArgumentException("A monotonic deadline is required.", nameof(deadline));
        if (!Enum.IsDefined(priority)) throw new ArgumentException("A registered priority is required.", nameof(priority));
        ArgumentNullException.ThrowIfNull(charges);
        var collected = new List<CapacityChargeV1>(MaximumCharges);
        foreach (var charge in charges)
        {
            if (collected.Count == MaximumCharges) throw new ArgumentOutOfRangeException(nameof(charges));
            collected.Add(charge ?? throw new ArgumentException("A charge cannot be null.", nameof(charges)));
        }
        if (collected.Count == 0) throw new ArgumentOutOfRangeException(nameof(charges));
        var owned = collected.ToArray();
        Array.Sort(owned, CapacityChargeComparerV1.Instance);
        for (var index = 1; index < owned.Length; index++)
            if (CapacityChargeComparerV1.Instance.Compare(owned[index - 1], owned[index]) == 0)
                throw new ArgumentException("Duplicate capacity charges are forbidden.", nameof(charges));
        OperationId = operationId;
        Authority = authority;
        Charges = Array.AsReadOnly(owned);
        Deadline = deadline;
        Priority = priority;
    }

    /// <summary>Gets the stable idempotency identity.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the sparse expected authority vector.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the canonical sorted, deeply owned charge set.</summary>
    public IReadOnlyList<CapacityChargeV1> Charges { get; }
    /// <summary>Gets the absolute monotonic deadline.</summary>
    public MonotonicStampV1 Deadline { get; }
    /// <summary>Gets the closed admission class.</summary>
    public CapacityPriorityV1 Priority { get; }
}

internal sealed class CapacityChargeComparerV1 : IComparer<CapacityChargeV1>
{
    internal static CapacityChargeComparerV1 Instance { get; } = new();

    public int Compare(CapacityChargeV1? left, CapacityChargeV1? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        var compared = left.DimensionId.Value.CompareTo(right.DimensionId.Value);
        if (compared != 0) return compared;
        compared = CompareScope(left.Scope, right.Scope);
        if (compared != 0) return compared;
        Span<byte> leftPurpose = stackalloc byte[16];
        Span<byte> rightPurpose = stackalloc byte[16];
        if (!left.Purpose.TryWriteBytes(leftPurpose) || !right.Purpose.TryWriteBytes(rightPurpose))
            throw new InvalidOperationException("A validated capacity purpose lost its canonical identity.");
        return leftPurpose.SequenceCompareTo(rightPurpose);
    }

    private static int CompareScope(CapacityScopeV1 left, CapacityScopeV1 right)
    {
        var leftBytes = CapacityScopeCanonicalCodecV1.Encode(left);
        var rightBytes = CapacityScopeCanonicalCodecV1.Encode(right);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }
}

internal static class CapacityScopeCanonicalCodecV1
{
    internal static byte[] Encode(CapacityScopeV1 scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1);
        writer.WriteUInt64((ushort)scope.Kind);
        writer.WriteUInt64(2);
        WriteStableId(writer, scope.TenantId.TryWriteBytes);
        writer.WriteUInt64(3);
        WriteOptionalSession(writer, scope.SessionId);
        writer.WriteUInt64(4);
        WriteOptionalSubject(writer, scope.Subject);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out CapacityScopeV1? scope)
    {
        scope = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) return false;
            var kind = checked((CapacityScopeKindV1)reader.ReadUInt64());
            if (reader.ReadUInt64() != 2) return false;
            var tenant = TenantId.FromValue(ReadStableId(reader));
            if (reader.ReadUInt64() != 3) return false;
            var session = ReadOptionalSession(reader);
            if (reader.ReadUInt64() != 4) return false;
            var subject = ReadOptionalSubject(reader);
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0) return false;
            scope = new CapacityScopeV1(tenant, session, subject);
            return scope.Kind == kind;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            scope = null;
            return false;
        }
    }

    private static void WriteOptionalSession(CborWriter writer, SessionId? session)
    {
        writer.WriteStartMap(session is null ? 1 : 2);
        writer.WriteUInt64(1);
        writer.WriteUInt64(session is null ? 0UL : 1UL);
        if (session is { } value)
        {
            writer.WriteUInt64(2);
            WriteStableId(writer, value.TryWriteBytes);
        }
        writer.WriteEndMap();
    }

    private static void WriteOptionalSubject(CborWriter writer, CapacitySubjectV1? subject)
    {
        writer.WriteStartMap(subject is null ? 1 : 2);
        writer.WriteUInt64(1);
        writer.WriteUInt64(subject is null ? 0UL : 1UL);
        if (subject is not null)
        {
            writer.WriteUInt64(2);
            writer.WriteStartMap(2);
            writer.WriteUInt64(1);
            writer.WriteUInt64((ushort)subject.Kind);
            writer.WriteUInt64(2);
            writer.WriteStartMap(2);
            writer.WriteUInt64(1);
            writer.WriteUInt64((ushort)(subject is CapacitySubjectV1.Owner ? CapacitySubjectValueKindV1.OwnerSlice : CapacitySubjectValueKindV1.StableId));
            writer.WriteUInt64(2);
            if (subject is CapacitySubjectV1.Owner owner)
                writer.WriteUInt64((ushort)owner.Value);
            else
            {
                Span<byte> identity = stackalloc byte[16];
                if (!subject.TryWriteIdentity(identity, out var written) || written != 16)
                    throw new InvalidOperationException("A validated subject lost its canonical identity.");
                writer.WriteByteString(identity);
            }
            writer.WriteEndMap();
            writer.WriteEndMap();
        }
        writer.WriteEndMap();
    }

    private static void WriteStableId(CborWriter writer, TryWriteStableId write)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!write(bytes)) throw new InvalidOperationException("A validated scope lost its canonical identity.");
        writer.WriteByteString(bytes);
    }

    private static StableId128 ReadStableId(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var written) || written != 16)
            throw new CborContentException("A capacity identity is exactly 16 bytes.");
        return StableId128.FromBytes(bytes);
    }

    private static SessionId? ReadOptionalSession(CborReader reader)
    {
        var count = reader.ReadStartMap();
        if (count is not (1 or 2) || reader.ReadUInt64() != 1) throw new CborContentException("Invalid optional session.");
        var present = reader.ReadUInt64();
        SessionId? value = present switch
        {
            0 when count == 1 => null,
            1 when count == 2 && reader.ReadUInt64() == 2 => SessionId.FromValue(ReadStableId(reader)),
            _ => throw new CborContentException("Invalid optional session arm."),
        };
        reader.ReadEndMap();
        return value;
    }

    private static CapacitySubjectV1? ReadOptionalSubject(CborReader reader)
    {
        var count = reader.ReadStartMap();
        if (count is not (1 or 2) || reader.ReadUInt64() != 1) throw new CborContentException("Invalid optional subject.");
        var present = reader.ReadUInt64();
        if (present == 0 && count == 1) { reader.ReadEndMap(); return null; }
        if (present != 1 || count != 2 || reader.ReadUInt64() != 2 || reader.ReadStartMap() != 2 || reader.ReadUInt64() != 1)
            throw new CborContentException("Invalid optional subject arm.");
        var kind = checked((CapacitySubjectKindV1)reader.ReadUInt64());
        if (reader.ReadUInt64() != 2 || reader.ReadStartMap() != 2 || reader.ReadUInt64() != 1)
            throw new CborContentException("Invalid subject value union.");
        var valueKind = checked((CapacitySubjectValueKindV1)reader.ReadUInt64());
        if (reader.ReadUInt64() != 2) throw new CborContentException("Invalid subject value tag.");
        CapacitySubjectV1 subject = (kind, valueKind) switch
        {
            (CapacitySubjectKindV1.Tenant, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Tenant(TenantId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Session, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Session(SessionId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Participant, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Participant(ParticipantId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Operation, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Operation(OperationId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Provider, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Provider(ProviderId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Custodian, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Custodian(CustodianDescriptorId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Exporter, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Exporter(ExportId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Subscriber, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Subscriber(SubscriberId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Schema, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Schema(SchemaId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Sink, CapacitySubjectValueKindV1.StableId) => new CapacitySubjectV1.Sink(SinkGenerationId.FromValue(ReadStableId(reader))),
            (CapacitySubjectKindV1.Owner, CapacitySubjectValueKindV1.OwnerSlice) => new CapacitySubjectV1.Owner(checked((OwnerSliceId)reader.ReadUInt64())),
            _ => throw new CborContentException("The subject kind/value arm is not registered."),
        };
        reader.ReadEndMap();
        reader.ReadEndMap();
        reader.ReadEndMap();
        return subject;
    }

    private delegate bool TryWriteStableId(Span<byte> destination);
}
