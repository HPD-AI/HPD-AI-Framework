using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Contains the bounded S9 authorization terms proposed before a capture grant is admitted.</summary>
public sealed record CaptureAuthorizationBodyV1
{
    /// <summary>Initializes exact reusable capture-grant terms without granting capture.</summary>
    public CaptureAuthorizationBodyV1(OperationId operationId, CaptureGrantId grantId, AuthorizationId authorizationId,
        Hash256 scopeHash, Hash256 limitsHash, UtcInstant expiresAt)
    {
        if (!operationId.IsValid || !grantId.IsValid || !authorizationId.IsValid)
            throw new ArgumentException("Capture authorization identities must be non-default.");
        Require(scopeHash, nameof(scopeHash)); Require(limitsHash, nameof(limitsHash));
        OperationId = operationId; GrantId = grantId; AuthorizationId = authorizationId;
        ScopeHash = scopeHash; LimitsHash = limitsHash; ExpiresAt = expiresAt;
    }

    /// <summary>Gets the retry and idempotency identity.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the S9-allocated reusable grant identity.</summary>
    public CaptureGrantId GrantId { get; }
    /// <summary>Gets the governing authorization identity.</summary>
    public AuthorizationId AuthorizationId { get; }
    /// <summary>Gets the canonical subject, purpose, audience and classification scope hash.</summary>
    public Hash256 ScopeHash { get; }
    /// <summary>Gets the canonical item, byte, range and time limit hash.</summary>
    public Hash256 LimitsHash { get; }
    /// <summary>Gets the UTC permission cap.</summary>
    public UtcInstant ExpiresAt { get; }

    private static void Require(Hash256 value, string name)
    { Span<byte> bytes = stackalloc byte[32]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("A canonical hash is required.", name); }
}

/// <summary>Wraps one S9 capture-authorization proposal in its exact session and authority fence.</summary>
public sealed record CaptureAuthorizationCommandV1
{
    internal CaptureAuthorizationCommandV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority,
        CaptureAuthorizationBodyV1 body)
    {
        if (!session.IsValid) throw new ArgumentException("A session authority stamp is required.", nameof(session));
        ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(body);
        if (authority.Session != session) throw new ArgumentException("The authority vector must belong to the command session.", nameof(authority));
        Session = session; Authority = authority; Body = body;
    }
    /// <summary>Gets the S1 session stamp.</summary>
    public SessionAuthorityStampV1 Session { get; }
    /// <summary>Gets the sparse owner axes validated for authorization.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the deeply owned authorization terms.</summary>
    public CaptureAuthorizationBodyV1 Body { get; }
}

/// <summary>Identifies the S9 P2 result disposition without upgrading rejection to a grant.</summary>
public enum CaptureGrantCommitDispositionV1 : ushort
{
    /// <summary>The exact source command was admitted as an active reusable grant.</summary>
    Granted = 1,
    /// <summary>The source command was rejected before capture could occur.</summary>
    Rejected = 2,
}

/// <summary>Binds one admitted S9 P2 result to its exact source command and authority vector.</summary>
public sealed record CaptureGrantCommittedV1
{
    internal CaptureGrantCommittedV1(OperationId operationId, JournalPositionV1 sourcePosition,
        ExpectedAuthorityVectorV1 authority, CaptureGrantCommitDispositionV1 disposition)
    {
        if (!operationId.IsValid || !sourcePosition.IsValid) throw new ArgumentException("A capture result requires operation and source position.");
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Session != sourcePosition.Session) throw new ArgumentException("The authority and source sessions must match.", nameof(authority));
        if (!Enum.IsDefined(disposition)) throw new ArgumentException("The disposition is outside the closed registry.", nameof(disposition));
        OperationId = operationId; SourcePosition = sourcePosition; Authority = authority; Disposition = disposition;
    }
    /// <summary>Gets the source command operation identity.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the exact admitted source command position.</summary>
    public JournalPositionV1 SourcePosition { get; }
    /// <summary>Gets the authority vector used for the decision.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the non-ambiguous grant disposition.</summary>
    public CaptureGrantCommitDispositionV1 Disposition { get; }
}

internal static class CaptureGrantCodecsV1
{
    internal const string CommandSchemaId = "hpd.authority-payload-capture-authorization-command.v1";
    internal const string FactSchemaId = "hpd.capture-grant-committed.v1";
    internal const ushort Major = 1, Minor = 0;
    internal const int MaximumCommandBytes = 512, MaximumFactBytes = 256;

    internal static byte[] EncodeCommand(CaptureAuthorizationCommandV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Session));
        writer.WriteUInt64(2); writer.WriteEncodedValue(value.Authority.GetCanonicalBytes());
        writer.WriteUInt64(3); writer.WriteByteString(EncodeBody(value.Body));
        writer.WriteEndMap(); return writer.Encode();
    }

    internal static bool TryDecodeCommand(ReadOnlyMemory<byte> encoded, out CaptureAuthorizationCommandV1? value)
    {
        value = null;
        try
        {
            var reader = Reader(encoded); RequireMap(reader, 3, 1);
            if (!SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session) || reader.ReadUInt64() != 2 ||
                !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) || reader.ReadUInt64() != 3 ||
                !TryDecodeBody(reader.ReadByteString(), out var body)) return false;
            reader.ReadEndMap(); if (reader.BytesRemaining != 0 || authority!.Session != session) return false;
            value = new CaptureAuthorizationCommandV1(session, authority, body!); return EncodeCommand(value).AsSpan().SequenceEqual(encoded.Span);
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    internal static byte[] EncodeFact(CaptureGrantCommittedV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4); writer.WriteUInt64(1); WriteId(writer, value.OperationId);
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(value.SourcePosition));
        writer.WriteUInt64(3); writer.WriteEncodedValue(value.Authority.GetCanonicalBytes());
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)value.Disposition); writer.WriteEndMap(); return writer.Encode();
    }

    internal static bool TryDecodeFact(ReadOnlyMemory<byte> encoded, out CaptureGrantCommittedV1? value)
    {
        value = null;
        try
        {
            var reader = Reader(encoded); RequireMap(reader, 4, 1); var operation = OperationId.FromValue(ReadId(reader));
            if (reader.ReadUInt64() != 2 || !AuthorityPositionCodecsV1.TryDecodeJournal(reader.ReadEncodedValue(), out var source) ||
                reader.ReadUInt64() != 3 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) || reader.ReadUInt64() != 4) return false;
            var disposition = (CaptureGrantCommitDispositionV1)checked((ushort)reader.ReadUInt64()); reader.ReadEndMap();
            if (reader.BytesRemaining != 0) return false; value = new(operation, source, authority!, disposition);
            return EncodeFact(value).AsSpan().SequenceEqual(encoded.Span);
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    internal static Hash256 CommandHash(CaptureAuthorizationCommandV1 value) =>
        AuthorityIntegrityHashV1.Compute(CommandSchemaId, Major, Minor, EncodeCommand(value));
    internal static Hash256 FactHash(CaptureGrantCommittedV1 value) =>
        AuthorityIntegrityHashV1.Compute(FactSchemaId, Major, Minor, EncodeFact(value));

    private static byte[] EncodeBody(CaptureAuthorizationBodyV1 value)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(6);
        writer.WriteUInt64(1); WriteId(writer, value.OperationId); writer.WriteUInt64(2); WriteId(writer, value.GrantId);
        writer.WriteUInt64(3); WriteId(writer, value.AuthorizationId); writer.WriteUInt64(4); WriteHash(writer, value.ScopeHash);
        writer.WriteUInt64(5); WriteHash(writer, value.LimitsHash); writer.WriteUInt64(6); writer.WriteInt64(value.ExpiresAt.NanosecondsSinceUnixEpoch);
        writer.WriteEndMap(); return writer.Encode();
    }

    private static bool TryDecodeBody(ReadOnlyMemory<byte> encoded, out CaptureAuthorizationBodyV1? value)
    {
        value = null; var reader = Reader(encoded); RequireMap(reader, 6, 1); var operation = OperationId.FromValue(ReadId(reader));
        if (reader.ReadUInt64() != 2) return false; var grant = CaptureGrantId.FromValue(ReadId(reader));
        if (reader.ReadUInt64() != 3) return false; var authorization = AuthorizationId.FromValue(ReadId(reader));
        if (reader.ReadUInt64() != 4) return false; var scope = ReadHash(reader);
        if (reader.ReadUInt64() != 5) return false; var limits = ReadHash(reader);
        if (reader.ReadUInt64() != 6) return false; var expiry = new UtcInstant(reader.ReadInt64()); reader.ReadEndMap();
        if (reader.BytesRemaining != 0) return false; value = new(operation, grant, authorization, scope, limits, expiry);
        return EncodeBody(value).AsSpan().SequenceEqual(encoded.Span);
    }

    private static CborReader Reader(ReadOnlyMemory<byte> value) => new(value, CborConformanceMode.Ctap2Canonical, false);
    private static void RequireMap(CborReader reader, int count, ulong firstTag)
    { if (reader.ReadStartMap() != count || reader.ReadUInt64() != firstTag) throw new CborContentException("Unexpected canonical map shape."); }
    private static StableId128 ReadId(CborReader reader) { var bytes = reader.ReadByteString(); if (bytes.Length != 16) throw new CborContentException("An ID is 16 bytes."); return StableId128.FromBytes(bytes); }
    private static Hash256 ReadHash(CborReader reader) { var bytes = reader.ReadByteString(); if (!Hash256.TryCreate(bytes, out var value)) throw new CborContentException("A hash is 32 bytes."); return value; }
    private static void WriteId<T>(CborWriter writer, T value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; var ok = value switch { OperationId x => x.TryWriteBytes(bytes), CaptureGrantId x => x.TryWriteBytes(bytes), AuthorizationId x => x.TryWriteBytes(bytes), _ => false }; if (!ok) throw new ArgumentException("Invalid ID."); writer.WriteByteString(bytes); }
    private static void WriteHash(CborWriter writer, Hash256 value) { Span<byte> bytes = stackalloc byte[32]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("Invalid hash."); writer.WriteByteString(bytes); }
}
