using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract class InterruptionToolAuthorityPayload
{
    private readonly byte[] _body;

    protected InterruptionToolAuthorityPayload(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        InterruptionToolAuthorityPayloadCodec.Validate(session, expectedAuthority, body);
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

internal sealed class InterruptionCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : InterruptionToolAuthorityPayload(session, expectedAuthority, body);

internal sealed class InterruptionSettled(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : InterruptionToolAuthorityPayload(session, expectedAuthority, body);

internal sealed class ToolContinuation(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : InterruptionToolAuthorityPayload(session, expectedAuthority, body);

internal sealed class ToolEffectReceipt(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : InterruptionToolAuthorityPayload(session, expectedAuthority, body);

internal static class InterruptionToolAuthorityPayloadRegistrations
{
    internal const ushort InterruptionCommandDiscriminator = 19;
    internal const ushort InterruptionSettledDiscriminator = 20;
    internal const ushort ToolContinuationDiscriminator = 21;
    internal const ushort ToolEffectReceiptDiscriminator = 22;

    internal static readonly AuthorityPayloadRegistrationV1 InterruptionCommand = Register(
        InterruptionToolAuthorityPayloadCodec.InterruptionCommandSchemaId,
        static (payload, session) => InterruptionToolAuthorityPayloadCodec.TryDecodeInterruptionCommand(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 InterruptionSettled = Register(
        InterruptionToolAuthorityPayloadCodec.InterruptionSettledSchemaId,
        static (payload, session) => InterruptionToolAuthorityPayloadCodec.TryDecodeInterruptionSettled(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 ToolContinuation = Register(
        InterruptionToolAuthorityPayloadCodec.ToolContinuationSchemaId,
        static (payload, session) => InterruptionToolAuthorityPayloadCodec.TryDecodeToolContinuation(payload, out var value) && value!.Session == session);
    internal static readonly AuthorityPayloadRegistrationV1 ToolEffectReceipt = Register(
        InterruptionToolAuthorityPayloadCodec.ToolEffectReceiptSchemaId,
        static (payload, session) => InterruptionToolAuthorityPayloadCodec.TryDecodeToolEffectReceipt(payload, out var value) && value!.Session == session);

    private static AuthorityPayloadRegistrationV1 Register(
        string schema,
        Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(schema),
            InterruptionToolAuthorityPayloadCodec.Major,
            InterruptionToolAuthorityPayloadCodec.Minor,
            OwnerSliceId.S7,
            InterruptionToolAuthorityPayloadCodec.MaximumEncodedBytes,
            validator);
}

internal static class InterruptionToolAuthorityPayloadCodec
{
    private delegate T PayloadFactory<out T>(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body)
        where T : InterruptionToolAuthorityPayload;

    internal const string InterruptionCommandSchemaId = "hpd.authority-payload-interruption-command.v1";
    internal const string InterruptionSettledSchemaId = "hpd.authority-payload-interruption-settled.v1";
    internal const string ToolContinuationSchemaId = "hpd.authority-payload-tool-continuation.v1";
    internal const string ToolEffectReceiptSchemaId = "hpd.authority-payload-tool-effect-receipt.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(InterruptionCommand value) => EncodeValue(value);
    internal static byte[] Encode(InterruptionSettled value) => EncodeValue(value);
    internal static byte[] Encode(ToolContinuation value) => EncodeValue(value);
    internal static byte[] Encode(ToolEffectReceipt value) => EncodeValue(value);
    internal static bool TryDecodeInterruptionCommand(ReadOnlyMemory<byte> encoded, out InterruptionCommand? value) => TryDecode(encoded, static (s, a, b) => new InterruptionCommand(s, a, b), out value);
    internal static bool TryDecodeInterruptionSettled(ReadOnlyMemory<byte> encoded, out InterruptionSettled? value) => TryDecode(encoded, static (s, a, b) => new InterruptionSettled(s, a, b), out value);
    internal static bool TryDecodeToolContinuation(ReadOnlyMemory<byte> encoded, out ToolContinuation? value) => TryDecode(encoded, static (s, a, b) => new ToolContinuation(s, a, b), out value);
    internal static bool TryDecodeToolEffectReceipt(ReadOnlyMemory<byte> encoded, out ToolEffectReceipt? value) => TryDecode(encoded, static (s, a, b) => new ToolEffectReceipt(s, a, b), out value);
    internal static Hash256 ComputeHash(InterruptionCommand value) => ComputeHash(InterruptionCommandSchemaId, value);
    internal static Hash256 ComputeHash(InterruptionSettled value) => ComputeHash(InterruptionSettledSchemaId, value);
    internal static Hash256 ComputeHash(ToolContinuation value) => ComputeHash(ToolContinuationSchemaId, value);
    internal static Hash256 ComputeHash(ToolEffectReceipt value) => ComputeHash(ToolEffectReceiptSchemaId, value);

    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid interruption/tool authority payload.");
    }

    private static byte[] EncodeValue(InterruptionToolAuthorityPayload value)
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

    private static Hash256 ComputeHash(string schema, InterruptionToolAuthorityPayload value) =>
        AuthorityIntegrityHashV1.Compute(schema, Major, Minor, EncodeValue(value));

    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, PayloadFactory<T> factory, out T? value)
        where T : InterruptionToolAuthorityPayload
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
