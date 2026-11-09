using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract class PrivacyAuthorityPayload
{
    private readonly byte[] _body;

    protected PrivacyAuthorityPayload(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        PrivacyAuthorityPayloadCodec.Validate(session, expectedAuthority, body);
        Session = session;
        ExpectedAuthority = expectedAuthority;
        _body = body.ToArray();
        Body = Array.AsReadOnly(_body);
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal sealed class CopyReservationCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : PrivacyAuthorityPayload(session, expectedAuthority, body);

internal sealed class PrivacyDeleteEffect(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : PrivacyAuthorityPayload(session, expectedAuthority, body);

internal sealed class PrivacyCustodianReceipt(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : PrivacyAuthorityPayload(session, expectedAuthority, body);

internal static class PrivacyAuthorityPayloadRegistrations
{
    internal const ushort CopyReservationCommandDiscriminator = 26;
    internal const ushort PrivacyDeleteEffectDiscriminator = 27;
    internal const ushort PrivacyCustodianReceiptDiscriminator = 28;

    internal static readonly AuthorityPayloadRegistrationV1 CopyReservationCommand = Register(
        PrivacyAuthorityPayloadCodec.CopyReservationCommandSchemaId,
        static (payload, session) => PrivacyAuthorityPayloadCodec.TryDecodeCopyReservationCommand(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 PrivacyDeleteEffect = Register(
        PrivacyAuthorityPayloadCodec.PrivacyDeleteEffectSchemaId,
        static (payload, session) => PrivacyAuthorityPayloadCodec.TryDecodePrivacyDeleteEffect(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 PrivacyCustodianReceipt = Register(
        PrivacyAuthorityPayloadCodec.PrivacyCustodianReceiptSchemaId,
        static (payload, session) => PrivacyAuthorityPayloadCodec.TryDecodePrivacyCustodianReceipt(payload, out var value) && value!.Session == session);

    private static AuthorityPayloadRegistrationV1 Register(string schema, Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(schema), PrivacyAuthorityPayloadCodec.Major, PrivacyAuthorityPayloadCodec.Minor,
            OwnerSliceId.S9, PrivacyAuthorityPayloadCodec.MaximumEncodedBytes, validator);
}

internal static class PrivacyAuthorityPayloadCodec
{
    private delegate T PayloadFactory<out T>(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
        where T : PrivacyAuthorityPayload;

    internal const string CopyReservationCommandSchemaId = "hpd.authority-payload-copy-reservation-command.v1";
    internal const string PrivacyDeleteEffectSchemaId = "hpd.authority-payload-privacy-delete-effect.v1";
    internal const string PrivacyCustodianReceiptSchemaId = "hpd.authority-payload-privacy-custodian-receipt.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(CopyReservationCommand value) => EncodeValue(value);
    internal static byte[] Encode(PrivacyDeleteEffect value) => EncodeValue(value);
    internal static byte[] Encode(PrivacyCustodianReceipt value) => EncodeValue(value);
    internal static bool TryDecodeCopyReservationCommand(ReadOnlyMemory<byte> encoded, out CopyReservationCommand? value) => TryDecode(encoded, static (s, a, b) => new CopyReservationCommand(s, a, b), out value);
    internal static bool TryDecodePrivacyDeleteEffect(ReadOnlyMemory<byte> encoded, out PrivacyDeleteEffect? value) => TryDecode(encoded, static (s, a, b) => new PrivacyDeleteEffect(s, a, b), out value);
    internal static bool TryDecodePrivacyCustodianReceipt(ReadOnlyMemory<byte> encoded, out PrivacyCustodianReceipt? value) => TryDecode(encoded, static (s, a, b) => new PrivacyCustodianReceipt(s, a, b), out value);
    internal static Hash256 ComputeHash(CopyReservationCommand value) => ComputeHash(CopyReservationCommandSchemaId, value);
    internal static Hash256 ComputeHash(PrivacyDeleteEffect value) => ComputeHash(PrivacyDeleteEffectSchemaId, value);
    internal static Hash256 ComputeHash(PrivacyCustodianReceipt value) => ComputeHash(PrivacyCustodianReceiptSchemaId, value);

    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid privacy authority payload.");
    }

    private static byte[] EncodeValue(PrivacyAuthorityPayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Validate(value.Session, value.ExpectedAuthority, value.BodyBytes);
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

    private static Hash256 ComputeHash(string schema, PrivacyAuthorityPayload value) =>
        AuthorityIntegrityHashV1.Compute(schema, Major, Minor, EncodeValue(value));

    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, PayloadFactory<T> factory, out T? value)
        where T : PrivacyAuthorityPayload
    {
        value = null;
        if (encoded.Length is 0 or > MaximumEncodedBytes) return false;
        byte[]? rented = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1 ||
                !SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session) ||
                reader.ReadUInt64() != 2 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) ||
                reader.ReadUInt64() != 3) return false;
            rented = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes);
            if (!reader.TryReadByteString(rented, out var bodyLength) || bodyLength > MaximumBodyBytes) return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || authority!.Session != session) return false;
            value = factory(session, authority, rented.AsSpan(0, bodyLength));
            return encoded.Span.SequenceEqual(EncodeValue(value));
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }
}
