using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed class ProviderEffectCommandV1
{
    private readonly byte[] _body;

    internal ProviderEffectCommandV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        ProviderEffectAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
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

internal sealed class ProviderEffectReceiptV1
{
    private readonly byte[] _body;

    internal ProviderEffectReceiptV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        ProviderEffectAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
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

internal static class ProviderEffectAuthorityPayloadRegistrationsV1
{
    internal const ushort ProviderEffectCommandDiscriminator = 13;
    internal const ushort ProviderEffectReceiptDiscriminator = 14;

    internal static readonly AuthorityPayloadRegistrationV1 ProviderEffectCommand =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(ProviderEffectAuthorityPayloadCodecV1.ProviderEffectCommandSchemaId),
            ProviderEffectAuthorityPayloadCodecV1.Major,
            ProviderEffectAuthorityPayloadCodecV1.Minor,
            OwnerSliceId.S5,
            ProviderEffectAuthorityPayloadCodecV1.MaximumEncodedBytes,
            static (payload, session) =>
                ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectCommand(payload, out var value) &&
                value!.Session == session);

    internal static readonly AuthorityPayloadRegistrationV1 ProviderEffectReceipt =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(ProviderEffectAuthorityPayloadCodecV1.ProviderEffectReceiptSchemaId),
            ProviderEffectAuthorityPayloadCodecV1.Major,
            ProviderEffectAuthorityPayloadCodecV1.Minor,
            OwnerSliceId.S5,
            ProviderEffectAuthorityPayloadCodecV1.MaximumEncodedBytes,
            static (payload, session) =>
                ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectReceipt(payload, out var value) &&
                value!.Session == session);
}

internal static class ProviderEffectAuthorityPayloadCodecV1
{
    private delegate T PayloadFactory<out T>(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 authority,
        ReadOnlySpan<byte> body)
        where T : class;

    internal const string ProviderEffectCommandSchemaId = "hpd.authority-payload-provider-effect-command.v1";
    internal const string ProviderEffectReceiptSchemaId = "hpd.authority-payload-provider-effect-receipt.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(ProviderEffectCommandV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.Session, value.ExpectedAuthority, value.BodyBytes);
    }

    internal static byte[] Encode(ProviderEffectReceiptV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.Session, value.ExpectedAuthority, value.BodyBytes);
    }

    internal static bool TryDecodeProviderEffectCommand(
        ReadOnlyMemory<byte> encoded,
        out ProviderEffectCommandV1? value) =>
        TryDecode(encoded, static (session, authority, body) =>
            new ProviderEffectCommandV1(session, authority, body), out value);

    internal static bool TryDecodeProviderEffectReceipt(
        ReadOnlyMemory<byte> encoded,
        out ProviderEffectReceiptV1? value) =>
        TryDecode(encoded, static (session, authority, body) =>
            new ProviderEffectReceiptV1(session, authority, body), out value);

    internal static Hash256 ComputeHash(ProviderEffectCommandV1 value) =>
        AuthorityIntegrityHashV1.Compute(ProviderEffectCommandSchemaId, Major, Minor, Encode(value));

    internal static Hash256 ComputeHash(ProviderEffectReceiptV1 value) =>
        AuthorityIntegrityHashV1.Compute(ProviderEffectReceiptSchemaId, Major, Minor, Encode(value));

    internal static void Validate(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session ||
            body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid provider-effect authority payload.");
    }

    private static byte[] Encode(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        Validate(session, expectedAuthority, body);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        writer.WriteUInt64(2);
        writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(expectedAuthority));
        writer.WriteUInt64(3);
        writer.WriteByteString(body);
        writer.WriteEndMap();
        var result = writer.Encode();
        if (result.Length > MaximumEncodedBytes)
            throw new ArgumentOutOfRangeException(nameof(body));
        return result;
    }

    private static bool TryDecode<T>(
        ReadOnlyMemory<byte> encoded,
        PayloadFactory<T> factory,
        out T? value)
        where T : class
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
            return value switch
            {
                ProviderEffectCommandV1 command => encoded.Span.SequenceEqual(Encode(command)),
                ProviderEffectReceiptV1 receipt => encoded.Span.SequenceEqual(Encode(receipt)),
                _ => false,
            };
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or
                                           ArgumentException or OverflowException)
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
