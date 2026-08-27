using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed class SemanticCandidateV1
{
    private readonly byte[] _body;
    internal SemanticCandidateV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        SemanticCandidateAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray(); Body = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal static class SemanticCandidateAuthorityPayloadRegistrationV1
{
    internal const ushort Discriminator = 9;
    internal static readonly AuthorityPayloadRegistrationV1 Candidate = AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
        new BoundedAscii(SemanticCandidateAuthorityPayloadCodecV1.SchemaId), 1, 0, OwnerSliceId.S4,
        SemanticCandidateAuthorityPayloadCodecV1.MaximumEncodedBytes,
        static (payload, session) => SemanticCandidateAuthorityPayloadCodecV1.TryDecode(payload, out var value) && value!.Session == session);
}

internal static class SemanticCandidateAuthorityPayloadCodecV1
{
    internal const string SchemaId = "hpd.authority-payload-semantic-candidate.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;
    internal static byte[] Encode(SemanticCandidateV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); Validate(value.Session, value.ExpectedAuthority, value.BodyBytes);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(3);
        writer.WriteUInt64(1); writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Session));
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.ExpectedAuthority));
        writer.WriteUInt64(3); writer.WriteByteString(value.BodyBytes); writer.WriteEndMap();
        var result = writer.Encode(); if (result.Length > MaximumEncodedBytes) throw new ArgumentOutOfRangeException(nameof(value)); return result;
    }
    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out SemanticCandidateV1? value)
    {
        value = null; if (encoded.Length is 0 or > MaximumEncodedBytes) return false; byte[]? rented = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1 || !SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session) ||
                reader.ReadUInt64() != 2 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) || reader.ReadUInt64() != 3) return false;
            rented = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes); if (!reader.TryReadByteString(rented, out var bodyLength) || bodyLength > MaximumBodyBytes) return false;
            reader.ReadEndMap(); if (reader.BytesRemaining != 0 || authority!.Session != session) return false;
            value = new SemanticCandidateV1(session, authority, rented.AsSpan(0, bodyLength)); return encoded.Span.SequenceEqual(Encode(value));
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        { value = null; return false; }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true); }
    }
    internal static Hash256 ComputeHash(SemanticCandidateV1 value) => AuthorityIntegrityHashV1.Compute(SchemaId, Major, Minor, Encode(value));
    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid semantic-candidate authority payload.");
    }
}
