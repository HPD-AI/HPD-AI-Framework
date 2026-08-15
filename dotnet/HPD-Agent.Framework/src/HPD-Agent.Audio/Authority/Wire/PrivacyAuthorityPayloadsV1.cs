using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract class PrivacyAuthorityPayloadV1
{
    private readonly byte[] _body;

    protected PrivacyAuthorityPayloadV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        PrivacyAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
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

internal sealed class CopyReservationCommandV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : PrivacyAuthorityPayloadV1(session, expectedAuthority, body);

internal sealed class PrivacyDeleteEffectV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : PrivacyAuthorityPayloadV1(session, expectedAuthority, body);

internal sealed class PrivacyCustodianReceiptV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : PrivacyAuthorityPayloadV1(session, expectedAuthority, body);

internal static class PrivacyAuthorityPayloadRegistrationsV1
{
    internal const ushort CopyReservationCommandDiscriminator = 26;
    internal const ushort PrivacyDeleteEffectDiscriminator = 27;
    internal const ushort PrivacyCustodianReceiptDiscriminator = 28;

    internal static readonly AuthorityPayloadRegistrationV1 CopyReservationCommand = Register(
        PrivacyAuthorityPayloadCodecV1.CopyReservationCommandSchemaId,
        static (payload, session) => PrivacyAuthorityPayloadCodecV1.TryDecodeCopyReservationCommand(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 PrivacyDeleteEffect = Register(
        PrivacyAuthorityPayloadCodecV1.PrivacyDeleteEffectSchemaId,
        static (payload, session) => PrivacyAuthorityPayloadCodecV1.TryDecodePrivacyDeleteEffect(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 PrivacyCustodianReceipt = Register(
        PrivacyAuthorityPayloadCodecV1.PrivacyCustodianReceiptSchemaId,
        static (payload, session) => PrivacyAuthorityPayloadCodecV1.TryDecodePrivacyCustodianReceipt(payload, out var value) && value!.Session == session);

    private static AuthorityPayloadRegistrationV1 Register(string schema, Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(schema), PrivacyAuthorityPayloadCodecV1.Major, PrivacyAuthorityPayloadCodecV1.Minor,
            OwnerSliceId.S9, PrivacyAuthorityPayloadCodecV1.MaximumEncodedBytes, validator);
}

internal static class PrivacyAuthorityPayloadCodecV1
{
    private delegate T PayloadFactory<out T>(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
        where T : PrivacyAuthorityPayloadV1;

    internal const string CopyReservationCommandSchemaId = "hpd.authority-payload-copy-reservation-command.v1";
    internal const string PrivacyDeleteEffectSchemaId = "hpd.authority-payload-privacy-delete-effect.v1";
    internal const string PrivacyCustodianReceiptSchemaId = "hpd.authority-payload-privacy-custodian-receipt.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(CopyReservationCommandV1 value) => EncodeValue(value);
    internal static byte[] Encode(PrivacyDeleteEffectV1 value) => EncodeValue(value);
    internal static byte[] Encode(PrivacyCustodianReceiptV1 value) => EncodeValue(value);
    internal static bool TryDecodeCopyReservationCommand(ReadOnlyMemory<byte> encoded, out CopyReservationCommandV1? value) => TryDecode(encoded, static (s, a, b) => new CopyReservationCommandV1(s, a, b), out value);
    internal static bool TryDecodePrivacyDeleteEffect(ReadOnlyMemory<byte> encoded, out PrivacyDeleteEffectV1? value) => TryDecode(encoded, static (s, a, b) => new PrivacyDeleteEffectV1(s, a, b), out value);
    internal static bool TryDecodePrivacyCustodianReceipt(ReadOnlyMemory<byte> encoded, out PrivacyCustodianReceiptV1? value) => TryDecode(encoded, static (s, a, b) => new PrivacyCustodianReceiptV1(s, a, b), out value);
    internal static Hash256 ComputeHash(CopyReservationCommandV1 value) => ComputeHash(CopyReservationCommandSchemaId, value);
    internal static Hash256 ComputeHash(PrivacyDeleteEffectV1 value) => ComputeHash(PrivacyDeleteEffectSchemaId, value);
    internal static Hash256 ComputeHash(PrivacyCustodianReceiptV1 value) => ComputeHash(PrivacyCustodianReceiptSchemaId, value);

    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid privacy authority payload.");
    }

    private static byte[] EncodeValue(PrivacyAuthorityPayloadV1 value)
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

    private static Hash256 ComputeHash(string schema, PrivacyAuthorityPayloadV1 value) =>
        AuthorityIntegrityHashV1.Compute(schema, Major, Minor, EncodeValue(value));

    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, PayloadFactory<T> factory, out T? value)
        where T : PrivacyAuthorityPayloadV1
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
