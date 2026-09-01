using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Declares how one live session may coexist with other sessions in the same runtime generation.</summary>
public enum LiveAudioConcurrencyModeV1 : ushort
{
    /// <summary>The requested plan requires exclusive ownership of its declared resources.</summary>
    Exclusive = 1,
    /// <summary>The requested plan permits only explicitly compatible shared resources.</summary>
    SharedCompatible = 2,
}

/// <summary>Identifies a bounded pre-effect reason why a live-session reservation was refused.</summary>
public enum LiveAudioSessionStartRejectionV1 : ushort
{
    /// <summary>The S1 session authority stamp or one relevant owner axis is stale.</summary>
    StaleAuthority = 1,
    /// <summary>The S2 grant is absent, terminal, insufficient, or does not match the request.</summary>
    CapacityUnavailable = 2,
    /// <summary>The S9 capture grant is absent, expired, fenced, or does not match the request.</summary>
    CaptureUnauthorized = 3,
    /// <summary>The requested concurrency mode conflicts with an admitted reservation.</summary>
    ConcurrencyConflict = 4,
    /// <summary>The monotonic terminal deadline was already reached.</summary>
    DeadlineReached = 5,
    /// <summary>A participant factory or its owner configuration is not registered.</summary>
    ParticipantUnavailable = 6,
}

/// <summary>Represents the closed pre-effect result of reserving one live-session start request.</summary>
/// <remarks>No arm claims readiness, participant acquisition, capture, provider connection, or transport availability.</remarks>
public abstract record LiveAudioSessionStartResultV1
{
    private LiveAudioSessionStartResultV1() { }

    /// <summary>Reports the first durable S1 Starting reservation for the exact request fingerprint.</summary>
    public sealed record Reserved : LiveAudioSessionStartResultV1
    {
        internal Reserved(JournalPositionV1 position, Hash256 fingerprint) { Position = Require(position); Fingerprint = Require(fingerprint); }
        /// <summary>Gets the first admitted Starting fact position.</summary>
        public JournalPositionV1 Position { get; }
        /// <summary>Gets the exact canonical request fingerprint bound at that position.</summary>
        public Hash256 Fingerprint { get; }
    }

    /// <summary>Reports that an identical retry joined an existing durable reservation.</summary>
    public sealed record Joined : LiveAudioSessionStartResultV1
    {
        internal Joined(JournalPositionV1 position, Hash256 fingerprint) { Position = Require(position); Fingerprint = Require(fingerprint); }
        /// <summary>Gets the original admitted Starting fact position.</summary>
        public JournalPositionV1 Position { get; }
        /// <summary>Gets the canonical request fingerprint that matched the retry.</summary>
        public Hash256 Fingerprint { get; }
    }

    /// <summary>Reports that an operation or requested identity is already bound to different canonical bytes.</summary>
    public sealed record Conflict : LiveAudioSessionStartResultV1
    {
        internal Conflict(JournalPositionV1 existingPosition, Hash256 existingFingerprint)
        { ExistingPosition = Require(existingPosition); ExistingFingerprint = Require(existingFingerprint); }
        /// <summary>Gets the position of the incompatible existing reservation.</summary>
        public JournalPositionV1 ExistingPosition { get; }
        /// <summary>Gets the fingerprint already bound to the operation or requested identity.</summary>
        public Hash256 ExistingFingerprint { get; }
    }

    /// <summary>Reports a typed precondition failure before any reservation was admitted.</summary>
    public sealed record Rejected : LiveAudioSessionStartResultV1
    {
        internal Rejected(LiveAudioSessionStartRejectionV1 reason)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentException("The rejection reason is outside the closed registry.", nameof(reason));
            Reason = reason;
        }
        /// <summary>Gets the bounded pre-effect rejection reason.</summary>
        public LiveAudioSessionStartRejectionV1 Reason { get; }
    }

    /// <summary>Reports that commit versus noncommit must be reconciled through S1 by operation identity.</summary>
    public sealed record OutcomeUnknown : LiveAudioSessionStartResultV1
    {
        internal OutcomeUnknown(OperationId operationId, BoundedAscii safeCode)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            if (!safeCode.IsValid) throw new ArgumentException("A bounded safe code is required.", nameof(safeCode));
            OperationId = operationId; SafeCode = safeCode;
        }
        /// <summary>Gets the exact operation identity used for reconciliation.</summary>
        public OperationId OperationId { get; }
        /// <summary>Gets a bounded nonsecret diagnostic code that does not settle the outcome.</summary>
        public BoundedAscii SafeCode { get; }
    }

    private static JournalPositionV1 Require(JournalPositionV1 value) => value.IsValid ? value : throw new ArgumentException("An admitted position is required.", nameof(value));
    private static Hash256 Require(Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32];
        return value.TryWriteBytes(bytes) ? value : throw new ArgumentException("A request fingerprint is required.", nameof(value));
    }
}

/// <summary>Describes one generated participant factory requested by an inert live-session plan.</summary>
public sealed record LiveAudioParticipantSpecV1
{
    /// <summary>Initializes a bounded participant specification without constructing the participant.</summary>
    /// <param name="factoryKey">The generated application-catalog factory key.</param>
    /// <param name="owner">The S2 through S11 domain owner.</param>
    /// <param name="required">Whether failure must prevent readiness.</param>
    /// <param name="configurationHash">The owner-schema-bound immutable configuration hash.</param>
    public LiveAudioParticipantSpecV1(BoundedAscii factoryKey, OwnerSliceId owner, bool required, Hash256 configurationHash)
    {
        if (!factoryKey.IsValid) throw new ArgumentException("A participant factory key is required.", nameof(factoryKey));
        if (owner is < OwnerSliceId.S2 or > OwnerSliceId.S11)
            throw new ArgumentException("A participant must be owned by S2 through S11.", nameof(owner));
        Span<byte> hash = stackalloc byte[32];
        if (!configurationHash.TryWriteBytes(hash)) throw new ArgumentException("A configuration hash is required.", nameof(configurationHash));
        FactoryKey = factoryKey;
        Owner = owner;
        Required = required;
        ConfigurationHash = configurationHash;
    }

    /// <summary>Gets the generated participant factory key.</summary>
    public BoundedAscii FactoryKey { get; }
    /// <summary>Gets the domain owner.</summary>
    public OwnerSliceId Owner { get; }
    /// <summary>Gets whether readiness requires this participant.</summary>
    public bool Required { get; }
    /// <summary>Gets the immutable owner-configuration hash.</summary>
    public Hash256 ConfigurationHash { get; }
}

/// <summary>Contains the deeply owned, pre-effect request for one visible live-session reservation.</summary>
/// <remarks>Construction performs no provider, transport, device, network, journal, or participant effect.</remarks>
public sealed class LiveAudioSessionStartRequestV1
{
    /// <summary>The maximum number of participant specifications in one request.</summary>
    public const int MaximumParticipants = 32;
    private readonly LiveAudioParticipantSpecV1[] _participants;

    /// <summary>Initializes and fingerprints one structurally valid inert start request.</summary>
    /// <param name="operationId">The retry and idempotency identity.</param>
    /// <param name="requestedSessionId">An optional S1 identity reserved by the caller.</param>
    /// <param name="correlation">Bounded nonordering correlation whose operation must match.</param>
    /// <param name="planId">The immutable compiled-plan identity.</param>
    /// <param name="expectedAuthority">The exact session and sparse owner fences.</param>
    /// <param name="capacityGrant">The fact-derived S2 reservation projection.</param>
    /// <param name="captureGrant">The S9-admitted capture authorization projection.</param>
    /// <param name="concurrency">The closed concurrency mode.</param>
    /// <param name="terminalDeadline">The absolute monotonic containment deadline.</param>
    /// <param name="participants">One to 32 generated participant specifications.</param>
    public LiveAudioSessionStartRequestV1(
        OperationId operationId,
        LiveSessionId? requestedSessionId,
        CorrelationEnvelopeV1 correlation,
        LiveAudioPlanId planId,
        ExpectedAuthorityVectorV1 expectedAuthority,
        CapacityGrantSnapshotV1 capacityGrant,
        CaptureGrantProofV1 captureGrant,
        LiveAudioConcurrencyModeV1 concurrency,
        MonotonicStampV1 terminalDeadline,
        IEnumerable<LiveAudioParticipantSpecV1> participants)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        if (requestedSessionId is { IsValid: false }) throw new ArgumentException("A requested session identity must be valid.", nameof(requestedSessionId));
        if (!correlation.IsValid || correlation.OperationId != operationId)
            throw new ArgumentException("Correlation must contain the exact operation identity.", nameof(correlation));
        if (!planId.IsValid) throw new ArgumentException("A live Audio plan identity is required.", nameof(planId));
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        ArgumentNullException.ThrowIfNull(capacityGrant);
        ArgumentNullException.ThrowIfNull(captureGrant);
        if (capacityGrant.OperationId != operationId || capacityGrant.Authority != expectedAuthority ||
            capacityGrant.State is not (CapacityGrantStateV1.Reserved or CapacityGrantStateV1.Active) || capacityGrant.Balances.Count == 0)
            throw new ArgumentException("The S2 grant must be current, nonempty, and bound to this operation and authority.", nameof(capacityGrant));
        if (captureGrant.Authority != expectedAuthority || captureGrant.GrantedAt.Session != expectedAuthority.Session ||
            captureGrant.State != CaptureGrantStateV1.Active)
            throw new ArgumentException("The S9 capture proof must be active and bound to the same authority session.", nameof(captureGrant));
        if (!Enum.IsDefined(concurrency)) throw new ArgumentException("The concurrency mode is outside the closed registry.", nameof(concurrency));
        if (!terminalDeadline.IsValid) throw new ArgumentException("A monotonic terminal deadline is required.", nameof(terminalDeadline));
        _participants = OwnParticipants(participants);
        OperationId = operationId;
        RequestedSessionId = requestedSessionId;
        Correlation = correlation;
        PlanId = planId;
        ExpectedAuthority = expectedAuthority;
        CapacityGrant = capacityGrant;
        CaptureGrant = captureGrant;
        Concurrency = concurrency;
        TerminalDeadline = terminalDeadline;
        Participants = Array.AsReadOnly(_participants);
        Fingerprint = LiveAudioSessionStartRequestFingerprintV1.Compute(this);
    }

    /// <summary>Gets the retry and idempotency identity.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the optional caller-requested session identity.</summary>
    public LiveSessionId? RequestedSessionId { get; }
    /// <summary>Gets bounded nonordering correlation.</summary>
    public CorrelationEnvelopeV1 Correlation { get; }
    /// <summary>Gets the immutable compiled-plan identity.</summary>
    public LiveAudioPlanId PlanId { get; }
    /// <summary>Gets the exact session and sparse owner authority fences.</summary>
    public ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    /// <summary>Gets the fact-derived S2 capacity grant.</summary>
    public CapacityGrantSnapshotV1 CapacityGrant { get; }
    /// <summary>Gets the S9-admitted capture authorization proof.</summary>
    public CaptureGrantProofV1 CaptureGrant { get; }
    /// <summary>Gets the requested concurrency policy.</summary>
    public LiveAudioConcurrencyModeV1 Concurrency { get; }
    /// <summary>Gets the absolute monotonic terminal deadline.</summary>
    public MonotonicStampV1 TerminalDeadline { get; }
    /// <summary>Gets participant specifications in canonical factory-key order.</summary>
    public IReadOnlyList<LiveAudioParticipantSpecV1> Participants { get; }
    /// <summary>Gets the nested-domain-separated fingerprint of the canonical request bytes.</summary>
    public Hash256 Fingerprint { get; }

    internal byte[] GetCanonicalBytes() => LiveAudioSessionStartRequestFingerprintV1.Encode(this);

    private static LiveAudioParticipantSpecV1[] OwnParticipants(IEnumerable<LiveAudioParticipantSpecV1> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var values = new List<LiveAudioParticipantSpecV1>();
        foreach (var participant in participants)
        {
            if (values.Count == MaximumParticipants) throw new ArgumentOutOfRangeException(nameof(participants));
            values.Add(participant ?? throw new ArgumentException("A participant specification cannot be null.", nameof(participants)));
        }
        if (values.Count == 0) throw new ArgumentOutOfRangeException(nameof(participants));
        values.Sort(static (left, right) => left.FactoryKey.CompareTo(right.FactoryKey));
        for (var index = 1; index < values.Count; index++)
            if (values[index - 1].FactoryKey == values[index].FactoryKey)
                throw new ArgumentException("Participant factory keys must be unique.", nameof(participants));
        return values.ToArray();
    }
}

internal static class LiveAudioSessionStartRequestFingerprintV1
{
    internal static Hash256 Compute(LiveAudioSessionStartRequestV1 request)
    {
        var bytes = Encode(request);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.live-audio-session-start-request.v1@1.0\0"u8);
        hash.AppendData(bytes);
        if (!Hash256.TryCreate(hash.GetHashAndReset(), out var result)) throw new InvalidOperationException("SHA-256 returned an invalid digest length.");
        return result;
    }

    internal static byte[] Encode(LiveAudioSessionStartRequestV1 request)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(10);
        writer.WriteUInt64(1); WriteId(writer, request.OperationId);
        writer.WriteUInt64(2); WriteOptionalId(writer, request.RequestedSessionId);
        writer.WriteUInt64(3); WriteCorrelation(writer, request.Correlation);
        writer.WriteUInt64(4); WriteId(writer, request.PlanId);
        writer.WriteUInt64(5); writer.WriteEncodedValue(request.ExpectedAuthority.GetCanonicalBytes());
        writer.WriteUInt64(6); WriteCapacity(writer, request.CapacityGrant);
        writer.WriteUInt64(7); WriteCapture(writer, request.CaptureGrant);
        writer.WriteUInt64(8); writer.WriteUInt64((ushort)request.Concurrency);
        writer.WriteUInt64(9); WriteMonotonic(writer, request.TerminalDeadline);
        writer.WriteUInt64(10);
        writer.WriteStartArray(request.Participants.Count);
        foreach (var participant in request.Participants) WriteParticipant(writer, participant);
        writer.WriteEndArray();
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteParticipant(CborWriter writer, LiveAudioParticipantSpecV1 value)
    {
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); writer.WriteTextString(value.FactoryKey.ToString());
        writer.WriteUInt64(2); writer.WriteUInt64((ushort)value.Owner);
        writer.WriteUInt64(3); writer.WriteBoolean(value.Required);
        writer.WriteUInt64(4); WriteHash(writer, value.ConfigurationHash);
        writer.WriteEndMap();
    }

    private static void WriteCapacity(CborWriter writer, CapacityGrantSnapshotV1 value)
    {
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); WriteId(writer, value.GrantId);
        writer.WriteUInt64(2); WritePosition(writer, value.CurrentFact);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)value.State);
        writer.WriteEndMap();
    }

    private static void WriteCapture(CborWriter writer, CaptureGrantProofV1 value)
    {
        writer.WriteStartMap(8);
        writer.WriteUInt64(1); WriteId(writer, value.GrantId);
        writer.WriteUInt64(2); WriteId(writer, value.AuthorizationId);
        writer.WriteUInt64(3); WritePosition(writer, value.GrantedAt);
        writer.WriteUInt64(4); writer.WriteEncodedValue(value.Authority.GetCanonicalBytes());
        writer.WriteUInt64(5); WriteHash(writer, value.ScopeHash);
        writer.WriteUInt64(6); WriteHash(writer, value.LimitsHash);
        writer.WriteUInt64(7); writer.WriteInt64(value.ExpiresAt.NanosecondsSinceUnixEpoch);
        writer.WriteUInt64(8); writer.WriteUInt64((ushort)value.State);
        writer.WriteEndMap();
    }

    private static void WriteCorrelation(CborWriter writer, CorrelationEnvelopeV1 value)
    {
        writer.WriteStartMap(6);
        writer.WriteUInt64(1); WriteId(writer, value.TenantId);
        writer.WriteUInt64(2); WriteOptionalId(writer, value.PrincipalId);
        writer.WriteUInt64(3); WriteOptionalId(writer, value.SessionId);
        writer.WriteUInt64(4); WriteOptionalId(writer, value.ThreadId);
        writer.WriteUInt64(5); WriteOptionalId(writer, value.ParticipantId);
        writer.WriteUInt64(6); WriteOptionalId(writer, value.OperationId);
        writer.WriteEndMap();
    }

    private static void WritePosition(CborWriter writer, JournalPositionV1 value)
    {
        writer.WriteStartMap(2);
        writer.WriteUInt64(1);
        writer.WriteStartMap(2);
        writer.WriteUInt64(1); WriteId(writer, value.Session.RuntimeGenerationId);
        writer.WriteUInt64(2); WriteId(writer, value.Session.LiveSessionId);
        writer.WriteEndMap();
        writer.WriteUInt64(2); writer.WriteInt64(value.Sequence);
        writer.WriteEndMap();
    }

    private static void WriteMonotonic(CborWriter writer, MonotonicStampV1 value)
    {
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); WriteId(writer, value.ClockDomainId);
        writer.WriteUInt64(2); WriteId(writer, value.BootId);
        writer.WriteUInt64(3); writer.WriteUInt64(value.Nanoseconds);
        writer.WriteEndMap();
    }

    private static void WriteOptionalId<T>(CborWriter writer, T? value) where T : struct
    {
        writer.WriteStartMap(value.HasValue ? 2 : 1);
        writer.WriteUInt64(1); writer.WriteUInt64(value.HasValue ? 1UL : 0UL);
        if (value.HasValue) { writer.WriteUInt64(2); WriteId(writer, value.Value); }
        writer.WriteEndMap();
    }

    private static void WriteId<T>(CborWriter writer, T value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16];
        var written = value switch
        {
            OperationId item => item.TryWriteBytes(bytes), LiveSessionId item => item.TryWriteBytes(bytes),
            TenantId item => item.TryWriteBytes(bytes), PrincipalId item => item.TryWriteBytes(bytes),
            SessionId item => item.TryWriteBytes(bytes), ThreadId item => item.TryWriteBytes(bytes),
            ParticipantId item => item.TryWriteBytes(bytes), LiveAudioPlanId item => item.TryWriteBytes(bytes),
            CapacityGrantId item => item.TryWriteBytes(bytes), CaptureGrantId item => item.TryWriteBytes(bytes),
            AuthorizationId item => item.TryWriteBytes(bytes), RuntimeGenerationId item => item.TryWriteBytes(bytes),
            ClockDomainId item => item.TryWriteBytes(bytes), BootId item => item.TryWriteBytes(bytes),
            _ => false,
        };
        if (!written) throw new ArgumentException("An authority identity is invalid.", nameof(value));
        writer.WriteByteString(bytes);
    }

    private static void WriteHash(CborWriter writer, Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!value.TryWriteBytes(bytes)) throw new ArgumentException("An authority hash is invalid.", nameof(value));
        writer.WriteByteString(bytes);
    }
}
