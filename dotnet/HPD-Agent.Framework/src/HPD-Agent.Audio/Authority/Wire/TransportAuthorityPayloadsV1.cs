using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract class TransportAuthorityPayloadV1
{
    private readonly byte[] _body;
    protected TransportAuthorityPayloadV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        TransportAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray(); Body = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal sealed class TransportAdapterCommandV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TransportAuthorityPayloadV1(session, expectedAuthority, body);
internal sealed class TransportAdapterReceiptV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TransportAuthorityPayloadV1(session, expectedAuthority, body);

internal static class TransportAuthorityPayloadRegistrationsV1
{
    internal const ushort TransportAdapterCommandDiscriminator = 31;
    internal const ushort TransportAdapterReceiptDiscriminator = 32;
    internal static readonly AuthorityPayloadRegistrationV1 TransportAdapterCommand = Register(
        TransportAuthorityPayloadCodecV1.TransportAdapterCommandSchemaId,
        static (payload, session) => TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterCommand(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 TransportAdapterReceipt = Register(
        TransportAuthorityPayloadCodecV1.TransportAdapterReceiptSchemaId,
        static (payload, session) => TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterReceipt(payload, out var value) && value!.Session == session);
    private static AuthorityPayloadRegistrationV1 Register(string schema, Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(schema), 1, 0, OwnerSliceId.S11,
            TransportAuthorityPayloadCodecV1.MaximumEncodedBytes, validator);
}

internal static class TransportAuthorityPayloadCodecV1
{
    private delegate T PayloadFactory<out T>(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
        where T : TransportAuthorityPayloadV1;
    internal const string TransportAdapterCommandSchemaId = "hpd.authority-payload-transport-adapter-command.v1";
    internal const string TransportAdapterReceiptSchemaId = "hpd.authority-payload-transport-adapter-receipt.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;
    internal static byte[] Encode(TransportAdapterCommandV1 value) => EncodeValue(value);
    internal static byte[] Encode(TransportAdapterReceiptV1 value) => EncodeValue(value);
    internal static bool TryDecodeTransportAdapterCommand(ReadOnlyMemory<byte> encoded, out TransportAdapterCommandV1? value) => TryDecode(encoded, static (s, a, b) => new TransportAdapterCommandV1(s, a, b), out value);
    internal static bool TryDecodeTransportAdapterReceipt(ReadOnlyMemory<byte> encoded, out TransportAdapterReceiptV1? value) => TryDecode(encoded, static (s, a, b) => new TransportAdapterReceiptV1(s, a, b), out value);
    internal static Hash256 ComputeHash(TransportAdapterCommandV1 value) => AuthorityIntegrityHashV1.Compute(TransportAdapterCommandSchemaId, Major, Minor, Encode(value));
    internal static Hash256 ComputeHash(TransportAdapterReceiptV1 value) => AuthorityIntegrityHashV1.Compute(TransportAdapterReceiptSchemaId, Major, Minor, Encode(value));
    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid transport authority payload.");
    }
    private static byte[] EncodeValue(TransportAuthorityPayloadV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); Validate(value.Session, value.ExpectedAuthority, value.BodyBytes);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(3);
        writer.WriteUInt64(1); writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Session));
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.ExpectedAuthority));
        writer.WriteUInt64(3); writer.WriteByteString(value.BodyBytes); writer.WriteEndMap();
        var result = writer.Encode(); if (result.Length > MaximumEncodedBytes) throw new ArgumentOutOfRangeException(nameof(value)); return result;
    }
    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, PayloadFactory<T> factory, out T? value) where T : TransportAuthorityPayloadV1
    {
        value = null; if (encoded.Length is 0 or > MaximumEncodedBytes) return false; byte[]? rented = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1 || !SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session) ||
                reader.ReadUInt64() != 2 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) || reader.ReadUInt64() != 3) return false;
            rented = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes); if (!reader.TryReadByteString(rented, out var bodyLength) || bodyLength > MaximumBodyBytes) return false;
            reader.ReadEndMap(); if (reader.BytesRemaining != 0 || authority!.Session != session) return false;
            value = factory(session, authority, rented.AsSpan(0, bodyLength)); return encoded.Span.SequenceEqual(EncodeValue(value));
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        { value = null; return false; }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true); }
    }
}
