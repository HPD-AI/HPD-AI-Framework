using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

internal enum AuthorityPayloadAdmissionV1
{
    Exact,
    UnknownSchema,
    OwnerMismatch,
    InvalidPayload,
    HashMismatch,
}

internal abstract class AuthorityPayloadRegistrationV1
{
    private protected AuthorityPayloadRegistrationV1(
        BoundedAscii schemaToken,
        ushort major,
        ushort minor,
        OwnerSliceId owner,
        int maximumPayloadBytes)
    {
        if (!schemaToken.IsValid) throw new ArgumentException("A schema token is required.", nameof(schemaToken));
        if (major == 0) throw new ArgumentOutOfRangeException(nameof(major));
        if (!Enum.IsDefined(owner)) throw new ArgumentException("A registered owner is required.", nameof(owner));
        if (maximumPayloadBytes is < 0 or > ProposedAuthorityFactV1.MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        var prefix = schemaToken.ToString() + "|" + major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
            minor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|";
        var registered = AuthoritySchemaLedgerV1.Schemas.SingleOrDefault(row => row.StartsWith(prefix, StringComparison.Ordinal));
        if (registered is null || !string.Equals(registered.Split('|')[2], owner.ToString(), StringComparison.Ordinal))
            throw new ArgumentException("The schema token, version, and semantic owner must exactly join the generated authority registry.", nameof(schemaToken));
        Schema = new SchemaReferenceV1(AuthoritySchemaIdentityV1.Derive(schemaToken), major, minor);
        SchemaToken = schemaToken;
        Owner = owner;
        MaximumPayloadBytes = maximumPayloadBytes;
    }

    internal SchemaReferenceV1 Schema { get; }
    internal BoundedAscii SchemaToken { get; }
    internal OwnerSliceId Owner { get; }
    internal int MaximumPayloadBytes { get; }
    internal bool Validate(ReadOnlyMemory<byte> payload) =>
        payload.Length <= MaximumPayloadBytes && ValidateCanonicalPayload(payload, default);
    internal bool Validate(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        payload.Length <= MaximumPayloadBytes && ValidateCanonicalPayload(payload, session);
    private protected abstract bool ValidateCanonicalPayload(
        ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session);
}

internal sealed class SessionAuthorityStampPayloadRegistrationV1 : AuthorityPayloadRegistrationV1
{
    internal SessionAuthorityStampPayloadRegistrationV1() :
        base(new BoundedAscii(SessionAuthorityStampV1Codec.SchemaId), SessionAuthorityStampV1Codec.Major,
            SessionAuthorityStampV1Codec.Minor, OwnerSliceId.S1, 64) { }

    private protected override bool ValidateCanonicalPayload(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        SessionAuthorityStampV1Codec.TryDecode(payload, out _);
}

internal sealed class SemanticInputAcceptedPayloadRegistrationV1 : AuthorityPayloadRegistrationV1
{
    internal SemanticInputAcceptedPayloadRegistrationV1() :
        base(new BoundedAscii(SemanticInputAcceptedV1Codec.SchemaId), SemanticInputAcceptedV1Codec.Major,
            SemanticInputAcceptedV1Codec.Minor, OwnerSliceId.AgentCore, 4096) { }

    private protected override bool ValidateCanonicalPayload(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        SemanticInputAcceptedV1Codec.TryDecode(payload, out _);
}

internal sealed class SubmissionDispositionChosenPayloadRegistrationV1 : AuthorityPayloadRegistrationV1
{
    internal SubmissionDispositionChosenPayloadRegistrationV1() :
        base(new BoundedAscii(SubmissionDispositionChosenV1Codec.SchemaId), SubmissionDispositionChosenV1Codec.Major,
            SubmissionDispositionChosenV1Codec.Minor, OwnerSliceId.S1, 4096) { }

    private protected override bool ValidateCanonicalPayload(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        SubmissionDispositionChosenV1Codec.TryDecode(payload, out _);
}

internal sealed class AuthorityGenerationInitializationPayloadRegistrationV1 : AuthorityPayloadRegistrationV1
{
    private readonly AuthorityAxisId _axis;

    internal AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId axis) :
        base(AuthorityGenerationInitializationCodecV1.SchemaTokenFor(axis), 1, 0,
            AuthorityGenerationInitializationCodecV1.OwnerFor(axis), 128) => _axis = axis;

    private protected override bool ValidateCanonicalPayload(
        ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        AuthorityGenerationInitializationCodecV1.Decode(
            Schema, Owner, session, payload, out var decoded) == AuthorityGenerationInitializationDecodeV1.Valid &&
        decoded.Axis == _axis;
}

internal sealed class AuthorityGenerationTransitionPayloadRegistrationV1 : AuthorityPayloadRegistrationV1
{
    private readonly AuthorityAxisId _axis;

    internal AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId axis) :
        base(AuthorityGenerationTransitionCodecV1.SchemaTokenFor(axis), 1, 0,
            AuthorityGenerationTransitionCodecV1.OwnerFor(axis), 160) => _axis = axis;

    private protected override bool ValidateCanonicalPayload(
        ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        AuthorityGenerationTransitionCodecV1.Decode(
            Schema, Owner, session, payload, out var decoded) == AuthorityGenerationTransitionDecodeV1.Valid &&
        decoded.Axis == _axis;
}

internal static class AuthoritySchemaIdentityV1
{
    private static ReadOnlySpan<byte> Domain => "hpd-schema-id-v1\0"u8;

    internal static SchemaId Derive(BoundedAscii schemaToken)
    {
        if (!schemaToken.IsValid) throw new ArgumentException("A schema token is required.", nameof(schemaToken));
        var token = Encoding.ASCII.GetBytes(schemaToken.ToString());
        var preimage = new byte[Domain.Length + token.Length];
        Domain.CopyTo(preimage);
        token.CopyTo(preimage.AsSpan(Domain.Length));
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(preimage, digest);
        if (digest[..16].IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidOperationException("The registered schema token derived the reserved all-zero identity.");
        return SchemaId.FromValue(StableId128.FromBytes(digest[..16]));
    }
}

internal sealed class AuthorityPayloadAdmissionRegistryV1
{
    private readonly IReadOnlyDictionary<SchemaId, AuthorityPayloadRegistrationV1> _registrations;

    internal AuthorityPayloadAdmissionRegistryV1(IEnumerable<AuthorityPayloadRegistrationV1> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var map = new Dictionary<SchemaId, AuthorityPayloadRegistrationV1>();
        var tuples = new HashSet<(string Token, ushort Major, ushort Minor)>();
        foreach (var registration in registrations)
        {
            if (map.Count == 256) throw new ArgumentOutOfRangeException(nameof(registrations), "At most 256 exact payload codecs may be registered in one journal registry.");
            if (registration is null || !map.TryAdd(registration.Schema.SchemaId, registration) ||
                !tuples.Add((registration.SchemaToken.ToString(), registration.Schema.Major, registration.Schema.Minor)))
                throw new ArgumentException("Schema registrations must be nonnull and unique by stable identity and token-version tuple.", nameof(registrations));
        }
        if (map.Count == 0) throw new ArgumentOutOfRangeException(nameof(registrations));
        _registrations = map;
    }

    internal AuthorityPayloadAdmissionV1 Validate(
        SessionAuthorityStampV1 session,
        ProposedAuthorityFactV1 proposal,
        out AuthorityPayloadRegistrationV1? registration)
    {
        if (!_registrations.TryGetValue(proposal.PayloadSchema.SchemaId, out registration) || registration.Schema != proposal.PayloadSchema)
            return AuthorityPayloadAdmissionV1.UnknownSchema;
        if (registration.Owner != proposal.Owner)
            return AuthorityPayloadAdmissionV1.OwnerMismatch;
        if (!registration.Validate(proposal.PayloadMemory, session))
            return AuthorityPayloadAdmissionV1.InvalidPayload;
        return AuthorityPayloadHashV1.Compute(registration.SchemaToken, proposal.PayloadSchema, proposal.PayloadBytes) == proposal.PayloadHash
            ? AuthorityPayloadAdmissionV1.Exact
            : AuthorityPayloadAdmissionV1.HashMismatch;
    }
}

internal static class AuthorityPayloadHashV1
{
    internal static Hash256 Compute(BoundedAscii schemaToken, SchemaReferenceV1 schema, ReadOnlySpan<byte> canonicalPayload) =>
        AuthorityIntegrityHashV1.Compute(schemaToken.ToString(), schema.Major, schema.Minor, canonicalPayload);
}

internal static class AuthorityCanonicalCborV1
{
    internal static bool IsSingleCanonicalValue(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var reader = new CborReader(payload, CborConformanceMode.Ctap2Canonical, false);
            reader.SkipValue();
            return reader.BytesRemaining == 0;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static ulong GetAppendBatchEncodedLength(AppendAuthorityBatchV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ulong length = 1 + 5; // fixed map(5) and one-byte numeric keys 1..5
        length = checked(length + (ulong)SessionAuthorityStampV1Codec.Encode(request.Session).Length);
        length = checked(length + IntegerLength(request.ExpectedSessionHead));
        length = checked(length + ContainerLength((ulong)request.ExpectedThreadHeads.Count));
        foreach (var head in request.ExpectedThreadHeads)
            length = checked(length + (ulong)EncodeThreadExpectedHead(head).Length);
        length = checked(length + ContainerLength((ulong)request.Facts.Count));
        foreach (var fact in request.Facts)
            length = checked(length + (ulong)EncodeProposal(fact).Length);
        return checked(length + IntegerLength(request.MaximumEncodedBytes));
    }

    internal static byte[] EncodeAppendBatch(AppendAuthorityBatchV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5);
        writer.WriteUInt64(1);
        SessionAuthorityStampV1Codec.Write(writer, request.Session);
        writer.WriteUInt64(2);
        writer.WriteInt64(request.ExpectedSessionHead);
        writer.WriteUInt64(3);
        writer.WriteStartArray(request.ExpectedThreadHeads.Count);
        foreach (var head in request.ExpectedThreadHeads)
        {
            writer.WriteStartMap(3);
            writer.WriteUInt64(1); WriteId(writer, head.ThreadId);
            writer.WriteUInt64(2); writer.WriteInt64(head.Generation);
            writer.WriteUInt64(3); writer.WriteInt64(head.Sequence);
            writer.WriteEndMap();
        }
        writer.WriteEndArray();
        writer.WriteUInt64(4);
        writer.WriteStartArray(request.Facts.Count);
        foreach (var fact in request.Facts) WriteProposal(writer, fact);
        writer.WriteEndArray();
        writer.WriteUInt64(5);
        writer.WriteUInt64(request.MaximumEncodedBytes);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static byte[] EncodeEnvelopeWithoutIntegrity(
        ProposedAuthorityFactV1 fact,
        JournalPositionV1 position,
        ThreadPositionV1? threadPosition,
        UtcInstant admittedAt)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(11);
        writer.WriteUInt64(1); writer.WriteUInt64(AuthorityFactEnvelopeV1.SchemaVersion);
        writer.WriteUInt64(2); WriteId(writer, fact.FactId);
        writer.WriteUInt64(3); WriteJournalPosition(writer, position);
        writer.WriteUInt64(4); WriteThreadPositionUnion(writer, threadPosition);
        writer.WriteUInt64(5); writer.WriteUInt64((ushort)fact.Owner);
        writer.WriteUInt64(6); WriteSchema(writer, fact.PayloadSchema);
        writer.WriteUInt64(7); writer.WriteByteString(fact.PayloadBytes);
        writer.WriteUInt64(8); WriteHash(writer, fact.PayloadHash);
        writer.WriteUInt64(9); WriteCorrelation(writer, fact.Correlation);
        writer.WriteUInt64(10); writer.WriteInt64(fact.ObservedAt.NanosecondsSinceUnixEpoch);
        writer.WriteUInt64(11); writer.WriteInt64(admittedAt.NanosecondsSinceUnixEpoch);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static ulong GetEnvelopeEncodedLength(byte[] envelopeWithoutIntegrity, IntegrityEnvelopeV1 integrity)
    {
        ArgumentNullException.ThrowIfNull(envelopeWithoutIntegrity);
        ArgumentNullException.ThrowIfNull(integrity);
        if (envelopeWithoutIntegrity.Length == 0 || envelopeWithoutIntegrity[0] != 0xab)
            throw new ArgumentException("The envelope preimage must be the canonical eleven-field map.", nameof(envelopeWithoutIntegrity));
        return checked((ulong)envelopeWithoutIntegrity.Length + 1UL + (ulong)AuthorityEnvelopePrimitiveCodecsV1.Encode(integrity).Length);
    }

    private static void WriteProposal(CborWriter writer, ProposedAuthorityFactV1 fact)
    {
        writer.WriteStartMap(8);
        writer.WriteUInt64(1); WriteId(writer, fact.FactId);
        writer.WriteUInt64(2); WriteIdUnion(writer, fact.ThreadId);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)fact.Owner);
        writer.WriteUInt64(4); WriteSchema(writer, fact.PayloadSchema);
        writer.WriteUInt64(5); writer.WriteByteString(fact.PayloadBytes);
        writer.WriteUInt64(6); WriteHash(writer, fact.PayloadHash);
        writer.WriteUInt64(7); WriteCorrelation(writer, fact.Correlation);
        writer.WriteUInt64(8); writer.WriteInt64(fact.ObservedAt.NanosecondsSinceUnixEpoch);
        writer.WriteEndMap();
    }

    private static byte[] EncodeProposal(ProposedAuthorityFactV1 fact)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        WriteProposal(writer, fact);
        return writer.Encode();
    }

    private static byte[] EncodeThreadExpectedHead(ThreadExpectedHeadV1 head)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); WriteId(writer, head.ThreadId);
        writer.WriteUInt64(2); writer.WriteInt64(head.Generation);
        writer.WriteUInt64(3); writer.WriteInt64(head.Sequence);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static ulong IntegerLength(long value) => value >= 0
        ? IntegerLength((ulong)value)
        : IntegerLength((ulong)~value);

    private static ulong IntegerLength(ulong value) => value switch
    {
        < 24 => 1,
        <= byte.MaxValue => 2,
        <= ushort.MaxValue => 3,
        <= uint.MaxValue => 5,
        _ => 9,
    };

    private static ulong ContainerLength(ulong itemCount) => IntegerLength(itemCount);

    private static void WriteSchema(CborWriter writer, SchemaReferenceV1 schema)
    {
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); WriteId(writer, schema.SchemaId);
        writer.WriteUInt64(2); writer.WriteUInt64(schema.Major);
        writer.WriteUInt64(3); writer.WriteUInt64(schema.Minor);
        writer.WriteEndMap();
    }

    private static void WriteJournalPosition(CborWriter writer, JournalPositionV1 position)
    {
        writer.WriteStartMap(2);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, position.Session);
        writer.WriteUInt64(2); writer.WriteInt64(position.Sequence);
        writer.WriteEndMap();
    }

    private static void WriteThreadPositionUnion(CborWriter writer, ThreadPositionV1? position)
    {
        writer.WriteStartMap(position.HasValue ? 2 : 1);
        writer.WriteUInt64(1); writer.WriteUInt64(position.HasValue ? 1UL : 0UL);
        if (position is { } value)
        {
            writer.WriteUInt64(2);
            writer.WriteStartMap(3);
            writer.WriteUInt64(1); WriteId(writer, value.ThreadId);
            writer.WriteUInt64(2); writer.WriteInt64(value.Generation);
            writer.WriteUInt64(3); writer.WriteInt64(value.Sequence);
            writer.WriteEndMap();
        }
        writer.WriteEndMap();
    }

    private static void WriteIdUnion(CborWriter writer, ThreadId? id)
    {
        writer.WriteStartMap(id.HasValue ? 2 : 1);
        writer.WriteUInt64(1); writer.WriteUInt64(id.HasValue ? 1UL : 0UL);
        if (id is { } value) { writer.WriteUInt64(2); WriteId(writer, value); }
        writer.WriteEndMap();
    }

    private static void WriteCorrelation(CborWriter writer, CorrelationEnvelopeV1 correlation)
    {
        writer.WriteStartMap(6);
        writer.WriteUInt64(1); WriteId(writer, correlation.TenantId);
        writer.WriteUInt64(2); WriteOptionalId(writer, correlation.PrincipalId);
        writer.WriteUInt64(3); WriteOptionalId(writer, correlation.SessionId);
        writer.WriteUInt64(4); WriteOptionalId(writer, correlation.ThreadId);
        writer.WriteUInt64(5); WriteOptionalId(writer, correlation.ParticipantId);
        writer.WriteUInt64(6); WriteOptionalId(writer, correlation.OperationId);
        writer.WriteEndMap();
    }

    private static void WriteOptionalId<T>(CborWriter writer, T? id) where T : struct
    {
        writer.WriteStartMap(id.HasValue ? 2 : 1);
        writer.WriteUInt64(1); writer.WriteUInt64(id.HasValue ? 1UL : 0UL);
        if (id.HasValue)
        {
            writer.WriteUInt64(2);
            Span<byte> bytes = stackalloc byte[16];
            var success = id.Value switch
            {
                PrincipalId value => value.TryWriteBytes(bytes),
                SessionId value => value.TryWriteBytes(bytes),
                ThreadId value => value.TryWriteBytes(bytes),
                ParticipantId value => value.TryWriteBytes(bytes),
                OperationId value => value.TryWriteBytes(bytes),
                _ => false,
            };
            if (!success) throw new ArgumentException("An optional authority identity is invalid.", nameof(id));
            writer.WriteByteString(bytes);
        }
        writer.WriteEndMap();
    }

    private static void WriteHash(CborWriter writer, Hash256 hash)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!hash.TryWriteBytes(bytes)) throw new ArgumentException("An authority hash is invalid.", nameof(hash));
        writer.WriteByteString(bytes);
    }

    private static void WriteId<T>(CborWriter writer, T id) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16];
        var success = id switch
        {
            JournalFactId value => value.TryWriteBytes(bytes),
            ThreadId value => value.TryWriteBytes(bytes),
            TenantId value => value.TryWriteBytes(bytes),
            SchemaId value => value.TryWriteBytes(bytes),
            _ => false,
        };
        if (!success) throw new ArgumentException("An authority identity is invalid.", nameof(id));
        writer.WriteByteString(bytes);
    }
}
