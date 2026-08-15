using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract class OutputAuthorityPayloadV1
{
    private readonly byte[] _body;

    protected OutputAuthorityPayloadV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        OutputAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
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

internal sealed class OutputSinkCommandV1(
    SessionAuthorityStampV1 session,
    ExpectedAuthorityVectorV1 expectedAuthority,
    ReadOnlySpan<byte> body) : OutputAuthorityPayloadV1(session, expectedAuthority, body);

internal sealed class OutputSinkReceiptV1(
    SessionAuthorityStampV1 session,
    ExpectedAuthorityVectorV1 expectedAuthority,
    ReadOnlySpan<byte> body) : OutputAuthorityPayloadV1(session, expectedAuthority, body);

internal sealed class HeardRangeFactV1(
    SessionAuthorityStampV1 session,
    ExpectedAuthorityVectorV1 expectedAuthority,
    ReadOnlySpan<byte> body) : OutputAuthorityPayloadV1(session, expectedAuthority, body);

internal static class OutputAuthorityPayloadRegistrationsV1
{
    internal const ushort OutputSinkCommandDiscriminator = 16;
    internal const ushort OutputSinkReceiptDiscriminator = 17;
    internal const ushort HeardRangeFactDiscriminator = 18;

    internal static readonly AuthorityPayloadRegistrationV1 OutputSinkCommand = Register(
        OutputAuthorityPayloadCodecV1.OutputSinkCommandSchemaId,
        OwnerSliceId.S6,
        static (payload, session) => OutputAuthorityPayloadCodecV1.TryDecodeOutputSinkCommand(payload, out var value) && value!.Session == session);

    internal static readonly AuthorityPayloadRegistrationV1 OutputSinkReceipt = Register(
        OutputAuthorityPayloadCodecV1.OutputSinkReceiptSchemaId,
        OwnerSliceId.S6,
        static (payload, session) => OutputAuthorityPayloadCodecV1.TryDecodeOutputSinkReceipt(payload, out var value) && value!.Session == session);

    internal static readonly AuthorityPayloadRegistrationV1 HeardRangeFact = Register(
        OutputAuthorityPayloadCodecV1.HeardRangeFactSchemaId,
        OwnerSliceId.S6,
        static (payload, session) => OutputAuthorityPayloadCodecV1.TryDecodeHeardRangeFact(payload, out var value) && value!.Session == session);

    private static AuthorityPayloadRegistrationV1 Register(
        string schema,
        OwnerSliceId owner,
        Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(schema),
            OutputAuthorityPayloadCodecV1.Major,
            OutputAuthorityPayloadCodecV1.Minor,
            owner,
            OutputAuthorityPayloadCodecV1.MaximumEncodedBytes,
            validator);
}

internal static class OutputAuthorityPayloadCodecV1
{
    private delegate T PayloadFactory<out T>(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 authority,
        ReadOnlySpan<byte> body)
        where T : OutputAuthorityPayloadV1;

    internal const string OutputSinkCommandSchemaId = "hpd.authority-payload-output-sink-command.v1";
    internal const string OutputSinkReceiptSchemaId = "hpd.authority-payload-output-sink-receipt.v1";
    internal const string HeardRangeFactSchemaId = "hpd.authority-payload-heard-range-fact.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(OutputSinkCommandV1 value) => EncodeValue(value);
    internal static byte[] Encode(OutputSinkReceiptV1 value) => EncodeValue(value);
    internal static byte[] Encode(HeardRangeFactV1 value) => EncodeValue(value);

    internal static bool TryDecodeOutputSinkCommand(ReadOnlyMemory<byte> encoded, out OutputSinkCommandV1? value) =>
        TryDecode(encoded, static (session, authority, body) => new OutputSinkCommandV1(session, authority, body), out value);

    internal static bool TryDecodeOutputSinkReceipt(ReadOnlyMemory<byte> encoded, out OutputSinkReceiptV1? value) =>
        TryDecode(encoded, static (session, authority, body) => new OutputSinkReceiptV1(session, authority, body), out value);

    internal static bool TryDecodeHeardRangeFact(ReadOnlyMemory<byte> encoded, out HeardRangeFactV1? value) =>
        TryDecode(encoded, static (session, authority, body) => new HeardRangeFactV1(session, authority, body), out value);

    internal static Hash256 ComputeHash(OutputSinkCommandV1 value) => ComputeHash(OutputSinkCommandSchemaId, value);
    internal static Hash256 ComputeHash(OutputSinkReceiptV1 value) => ComputeHash(OutputSinkReceiptSchemaId, value);
    internal static Hash256 ComputeHash(HeardRangeFactV1 value) => ComputeHash(HeardRangeFactSchemaId, value);

    internal static void Validate(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid output authority payload.");
    }

    private static byte[] EncodeValue(OutputAuthorityPayloadV1 value)
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
        if (result.Length > MaximumEncodedBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        return result;
    }

    private static Hash256 ComputeHash(string schema, OutputAuthorityPayloadV1 value) =>
        AuthorityIntegrityHashV1.Compute(schema, Major, Minor, EncodeValue(value));

    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, PayloadFactory<T> factory, out T? value)
        where T : OutputAuthorityPayloadV1
    {
        value = null;
        if (encoded.Length is 0 or > MaximumEncodedBytes)
            return false;
        byte[]? rented = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1 ||
                !SessionAuthorityStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var session) ||
                reader.ReadUInt64() != 2 ||
                !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority) ||
                reader.ReadUInt64() != 3)
                return false;
            rented = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes);
            if (!reader.TryReadByteString(rented, out var bodyLength) || bodyLength > MaximumBodyBytes)
                return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || authority!.Session != session)
                return false;
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
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }
}
