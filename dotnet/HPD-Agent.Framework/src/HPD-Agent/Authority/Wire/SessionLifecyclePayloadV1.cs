using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Contains an S1-owned lifecycle command envelope with a bounded opaque owner-defined body.</summary>
/// <remarks>Construction proves structural validity only. The command becomes authority truth only after trusted S1.P0 journal admission.</remarks>
public sealed class SessionLifecycleCommandV1 : IEquatable<SessionLifecycleCommandV1>
{
    private readonly byte[] _body;

    /// <summary>Initializes a lifecycle command envelope and takes an owned copy of its body.</summary>
    /// <param name="session">The exact live-session authority stamp.</param>
    /// <param name="expectedAuthority">The sparse authority vector required by the command.</param>
    /// <param name="body">The opaque, owner-defined lifecycle command body.</param>
    /// <exception cref="ArgumentException">The session is invalid or differs from the authority vector session.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="expectedAuthority"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="body"/> exceeds 65,536 bytes.</exception>
    public SessionLifecycleCommandV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        Validate(session, expectedAuthority, body);
        Session = session;
        ExpectedAuthority = expectedAuthority;
        _body = body.ToArray();
        Body = Array.AsReadOnly(_body);
    }

    /// <summary>Gets the exact live-session authority stamp.</summary>
    public SessionAuthorityStampV1 Session { get; }

    /// <summary>Gets the sparse authority vector required by the command.</summary>
    public ExpectedAuthorityVectorV1 ExpectedAuthority { get; }

    /// <summary>Gets a read-only view of the owned opaque command body.</summary>
    public IReadOnlyList<byte> Body { get; }

    /// <inheritdoc />
    public bool Equals(SessionLifecycleCommandV1? other) =>
        other is not null && Session == other.Session && ExpectedAuthority == other.ExpectedAuthority &&
        _body.AsSpan().SequenceEqual(other._body);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SessionLifecycleCommandV1 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => SessionLifecyclePayloadV1Codec.GetHashCode(Session, ExpectedAuthority, _body);

    /// <summary>Returns whether two command envelopes have identical authority and body bytes.</summary>
    public static bool operator ==(SessionLifecycleCommandV1? left, SessionLifecycleCommandV1? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Returns whether two command envelopes differ in authority or body bytes.</summary>
    public static bool operator !=(SessionLifecycleCommandV1? left, SessionLifecycleCommandV1? right) => !(left == right);

    internal ReadOnlySpan<byte> BodyBytes => _body;

    private static void Validate(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid) throw new ArgumentException("A valid live-session authority stamp is required.", nameof(session));
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (expectedAuthority.Session != session)
            throw new ArgumentException("The command and authority-vector sessions must match.", nameof(expectedAuthority));
        if (body.Length > SessionLifecyclePayloadV1Codec.MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body), "A lifecycle body cannot exceed 65,536 bytes.");
    }
}

/// <summary>Contains an S1-owned lifecycle fact envelope with a bounded opaque owner-defined body.</summary>
/// <remarks>Construction proves structural validity only. The fact becomes authority truth only after trusted S1.P0 journal admission.</remarks>
public sealed class SessionLifecycleFactV1 : IEquatable<SessionLifecycleFactV1>
{
    private readonly byte[] _body;

    /// <summary>Initializes a lifecycle fact envelope and takes an owned copy of its body.</summary>
    /// <param name="session">The exact live-session authority stamp.</param>
    /// <param name="expectedAuthority">The sparse authority vector validated by the transition.</param>
    /// <param name="body">The opaque, owner-defined lifecycle fact body.</param>
    /// <exception cref="ArgumentException">The session is invalid or differs from the authority vector session.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="expectedAuthority"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="body"/> exceeds 65,536 bytes.</exception>
    public SessionLifecycleFactV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid) throw new ArgumentException("A valid live-session authority stamp is required.", nameof(session));
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (expectedAuthority.Session != session)
            throw new ArgumentException("The fact and authority-vector sessions must match.", nameof(expectedAuthority));
        if (body.Length > SessionLifecyclePayloadV1Codec.MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body), "A lifecycle body cannot exceed 65,536 bytes.");
        Session = session;
        ExpectedAuthority = expectedAuthority;
        _body = body.ToArray();
        Body = Array.AsReadOnly(_body);
    }

    /// <summary>Gets the exact live-session authority stamp.</summary>
    public SessionAuthorityStampV1 Session { get; }

    /// <summary>Gets the sparse authority vector validated by the transition.</summary>
    public ExpectedAuthorityVectorV1 ExpectedAuthority { get; }

    /// <summary>Gets a read-only view of the owned opaque fact body.</summary>
    public IReadOnlyList<byte> Body { get; }

    /// <inheritdoc />
    public bool Equals(SessionLifecycleFactV1? other) =>
        other is not null && Session == other.Session && ExpectedAuthority == other.ExpectedAuthority &&
        _body.AsSpan().SequenceEqual(other._body);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SessionLifecycleFactV1 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => SessionLifecyclePayloadV1Codec.GetHashCode(Session, ExpectedAuthority, _body);

    /// <summary>Returns whether two fact envelopes have identical authority and body bytes.</summary>
    public static bool operator ==(SessionLifecycleFactV1? left, SessionLifecycleFactV1? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Returns whether two fact envelopes differ in authority or body bytes.</summary>
    public static bool operator !=(SessionLifecycleFactV1? left, SessionLifecycleFactV1? right) => !(left == right);

    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal static class SessionLifecyclePayloadV1Codec
{
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 65_833;
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const string CommandSchemaId = "hpd.authority-payload-session-lifecycle-command.v1";
    internal const string FactSchemaId = "hpd.authority-payload-session-lifecycle-fact.v1";

    internal static byte[] Encode(SessionLifecycleCommandV1 value) =>
        EncodeCore(value?.Session ?? throw new ArgumentNullException(nameof(value)), value.ExpectedAuthority, value.BodyBytes);

    internal static byte[] Encode(SessionLifecycleFactV1 value) =>
        EncodeCore(value?.Session ?? throw new ArgumentNullException(nameof(value)), value.ExpectedAuthority, value.BodyBytes);

    internal static bool TryDecodeCommand(ReadOnlyMemory<byte> encoded, out SessionLifecycleCommandV1? value) =>
        TryDecodeCore(encoded, static (session, authority, body) => new SessionLifecycleCommandV1(session, authority, body.Span), out value);

    internal static bool TryDecodeFact(ReadOnlyMemory<byte> encoded, out SessionLifecycleFactV1? value) =>
        TryDecodeCore(encoded, static (session, authority, body) => new SessionLifecycleFactV1(session, authority, body.Span), out value);

    internal static Hash256 ComputeIntegrityHash(SessionLifecycleCommandV1 value) =>
        AuthorityIntegrityHashV1.Compute(CommandSchemaId, Major, Minor, Encode(value));

    internal static Hash256 ComputeIntegrityHash(SessionLifecycleFactV1 value) =>
        AuthorityIntegrityHashV1.Compute(FactSchemaId, Major, Minor, Encode(value));

    internal static int GetHashCode(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
    {
        var hash = new HashCode();
        hash.Add(session);
        hash.Add(authority);
        foreach (var item in body) hash.Add(item);
        return hash.ToHashCode();
    }

    private static byte[] EncodeCore(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); AuthorityVectorCodecsV1.WriteVector(writer, authority);
        writer.WriteUInt64(3); writer.WriteByteString(body);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static bool TryDecodeCore<T>(
        ReadOnlyMemory<byte> encoded,
        Func<SessionAuthorityStampV1, ExpectedAuthorityVectorV1, ReadOnlyMemory<byte>, T> factory,
        out T? value)
        where T : class
    {
        value = null;
        if (encoded.Length > MaximumEncodedBytes) return false;
        var bodyBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(MaximumBodyBytes);
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1) return false;
            var session = SessionAuthorityStampV1Codec.Read(reader);
            if (reader.ReadUInt64() != 2) return false;
            var authority = AuthorityVectorCodecsV1.ReadVector(reader);
            if (reader.ReadUInt64() != 3) return false;
            if (!reader.TryReadByteString(bodyBuffer, out var bodyLength)) return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || bodyLength > MaximumBodyBytes) return false;
            value = factory(session, authority, bodyBuffer.AsMemory(0, bodyLength));
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(bodyBuffer, clearArray: true);
        }
    }
}
