using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract class ReplayAuthorityPayloadV1
{
    private readonly byte[] _body;
    protected ReplayAuthorityPayloadV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        ReplayAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray(); Body = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal sealed class ReplayRunCommandV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : ReplayAuthorityPayloadV1(session, expectedAuthority, body);
internal sealed class ReplayEvidenceFactV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : ReplayAuthorityPayloadV1(session, expectedAuthority, body);

internal static class ReplayAuthorityPayloadRegistrationsV1
{
    internal const ushort ReplayRunCommandDiscriminator = 29;
    internal const ushort ReplayEvidenceFactDiscriminator = 30;
    internal static readonly AuthorityPayloadRegistrationV1 ReplayRunCommand = Register(
        ReplayAuthorityPayloadCodecV1.ReplayRunCommandSchemaId,
        static (payload, session) => ReplayAuthorityPayloadCodecV1.TryDecodeReplayRunCommand(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 ReplayEvidenceFact = Register(
        ReplayAuthorityPayloadCodecV1.ReplayEvidenceFactSchemaId,
        static (payload, session) => ReplayAuthorityPayloadCodecV1.TryDecodeReplayEvidenceFact(payload, out var value) && value!.Session == session);
    private static AuthorityPayloadRegistrationV1 Register(string schema, Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(schema), 1, 0, OwnerSliceId.S10,
            ReplayAuthorityPayloadCodecV1.MaximumEncodedBytes, validator);
}

internal static class ReplayAuthorityPayloadCodecV1
{
    private delegate T PayloadFactory<out T>(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
        where T : ReplayAuthorityPayloadV1;
    internal const string ReplayRunCommandSchemaId = "hpd.authority-payload-replay-run-command.v1";
    internal const string ReplayEvidenceFactSchemaId = "hpd.authority-payload-replay-evidence-fact.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(ReplayRunCommandV1 value) => EncodeValue(value);
    internal static byte[] Encode(ReplayEvidenceFactV1 value) => EncodeValue(value);
    internal static bool TryDecodeReplayRunCommand(ReadOnlyMemory<byte> encoded, out ReplayRunCommandV1? value) => TryDecode(encoded, static (s, a, b) => new ReplayRunCommandV1(s, a, b), out value);
    internal static bool TryDecodeReplayEvidenceFact(ReadOnlyMemory<byte> encoded, out ReplayEvidenceFactV1? value) => TryDecode(encoded, static (s, a, b) => new ReplayEvidenceFactV1(s, a, b), out value);
    internal static Hash256 ComputeHash(ReplayRunCommandV1 value) => AuthorityIntegrityHashV1.Compute(ReplayRunCommandSchemaId, Major, Minor, Encode(value));
    internal static Hash256 ComputeHash(ReplayEvidenceFactV1 value) => AuthorityIntegrityHashV1.Compute(ReplayEvidenceFactSchemaId, Major, Minor, Encode(value));

    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid replay authority payload.");
    }

    private static byte[] EncodeValue(ReplayAuthorityPayloadV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); Validate(value.Session, value.ExpectedAuthority, value.BodyBytes);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Session));
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.ExpectedAuthority));
        writer.WriteUInt64(3); writer.WriteByteString(value.BodyBytes);
        writer.WriteEndMap();
        var result = writer.Encode();
        if (result.Length > MaximumEncodedBytes) throw new ArgumentOutOfRangeException(nameof(value));
        return result;
    }

    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, PayloadFactory<T> factory, out T? value) where T : ReplayAuthorityPayloadV1
    {
        value = null;
        if (encoded.Length is 0 or > MaximumEncodedBytes) return false;
        byte[]? rented = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1 || !SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session) ||
                reader.ReadUInt64() != 2 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) || reader.ReadUInt64() != 3) return false;
            rented = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes);
            if (!reader.TryReadByteString(rented, out var bodyLength) || bodyLength > MaximumBodyBytes) return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || authority!.Session != session) return false;
            value = factory(session, authority, rented.AsSpan(0, bodyLength));
            return encoded.Span.SequenceEqual(EncodeValue(value));
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        { value = null; return false; }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true); }
    }
}
