using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed class VadObservation
{
    private readonly byte[] _body;

    internal VadObservation(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        ActivityAuthorityPayloadCodec.Validate(session, expectedAuthority, body);
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

internal sealed class ActivityBoundaryFact
{
    private readonly byte[] _body;

    internal ActivityBoundaryFact(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        ActivityAuthorityPayloadCodec.Validate(session, expectedAuthority, body);
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

internal static class ActivityAuthorityPayloadRegistrations
{
    internal const ushort VadObservationDiscriminator = 7;
    internal const ushort ActivityBoundaryFactDiscriminator = 8;

    internal static readonly AuthorityPayloadRegistrationV1 VadObservation =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(ActivityAuthorityPayloadCodec.VadObservationSchemaId),
            ActivityAuthorityPayloadCodec.Major,
            ActivityAuthorityPayloadCodec.Minor,
            OwnerSliceId.S3,
            ActivityAuthorityPayloadCodec.MaximumEncodedBytes,
            static (payload, session) =>
                ActivityAuthorityPayloadCodec.TryDecodeVadObservation(payload, out var value) &&
                value!.Session == session);

    internal static readonly AuthorityPayloadRegistrationV1 ActivityBoundaryFact =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(ActivityAuthorityPayloadCodec.ActivityBoundaryFactSchemaId),
            ActivityAuthorityPayloadCodec.Major,
            ActivityAuthorityPayloadCodec.Minor,
            OwnerSliceId.S3,
            ActivityAuthorityPayloadCodec.MaximumEncodedBytes,
            static (payload, session) =>
                ActivityAuthorityPayloadCodec.TryDecodeActivityBoundaryFact(payload, out var value) &&
                value!.Session == session);
}

internal static class ActivityAuthorityPayloadCodec
{
    private delegate T PayloadFactory<out T>(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 authority,
        ReadOnlySpan<byte> body)
        where T : class;

    internal const string VadObservationSchemaId = "hpd.authority-payload-vad-observation.v1";
    internal const string ActivityBoundaryFactSchemaId = "hpd.authority-payload-activity-boundary-fact.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumBodyBytes = 65_536;
    internal const int MaximumEncodedBytes = 66_560;

    internal static byte[] Encode(VadObservation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.Session, value.ExpectedAuthority, value.BodyBytes);
    }

    internal static byte[] Encode(ActivityBoundaryFact value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.Session, value.ExpectedAuthority, value.BodyBytes);
    }

    internal static bool TryDecodeVadObservation(ReadOnlyMemory<byte> encoded, out VadObservation? value) =>
        TryDecode(encoded, static (session, authority, body) => new VadObservation(session, authority, body), out value);

    internal static bool TryDecodeActivityBoundaryFact(ReadOnlyMemory<byte> encoded, out ActivityBoundaryFact? value) =>
        TryDecode(encoded, static (session, authority, body) => new ActivityBoundaryFact(session, authority, body), out value);

    internal static Hash256 ComputeHash(VadObservation value) =>
        AuthorityIntegrityHashV1.Compute(VadObservationSchemaId, Major, Minor, Encode(value));

    internal static Hash256 ComputeHash(ActivityBoundaryFact value) =>
        AuthorityIntegrityHashV1.Compute(ActivityBoundaryFactSchemaId, Major, Minor, Encode(value));

    internal static void Validate(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session ||
            body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid activity authority payload.");
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
                VadObservation observation => encoded.Span.SequenceEqual(Encode(observation)),
                ActivityBoundaryFact boundary => encoded.Span.SequenceEqual(Encode(boundary)),
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
