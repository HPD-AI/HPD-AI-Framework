using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed class VadObservationV1
{
    private readonly byte[] _body;

    internal VadObservationV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        ActivityAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
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

internal sealed class ActivityBoundaryFactV1
{
    private readonly byte[] _body;

    internal ActivityBoundaryFactV1(
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        ActivityAuthorityPayloadCodecV1.Validate(session, expectedAuthority, body);
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

internal static class ActivityAuthorityPayloadRegistrationsV1
{
    internal const ushort VadObservationDiscriminator = 7;
    internal const ushort ActivityBoundaryFactDiscriminator = 8;

    internal static readonly AuthorityPayloadRegistrationV1 VadObservation =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(ActivityAuthorityPayloadCodecV1.VadObservationSchemaId),
            ActivityAuthorityPayloadCodecV1.Major,
            ActivityAuthorityPayloadCodecV1.Minor,
            OwnerSliceId.S3,
            ActivityAuthorityPayloadCodecV1.MaximumEncodedBytes,
            static (payload, session) =>
                ActivityAuthorityPayloadCodecV1.TryDecodeVadObservation(payload, out var value) &&
                value!.Session == session);

    internal static readonly AuthorityPayloadRegistrationV1 ActivityBoundaryFact =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(ActivityAuthorityPayloadCodecV1.ActivityBoundaryFactSchemaId),
            ActivityAuthorityPayloadCodecV1.Major,
            ActivityAuthorityPayloadCodecV1.Minor,
            OwnerSliceId.S3,
            ActivityAuthorityPayloadCodecV1.MaximumEncodedBytes,
            static (payload, session) =>
                ActivityAuthorityPayloadCodecV1.TryDecodeActivityBoundaryFact(payload, out var value) &&
                value!.Session == session);
}

internal static class ActivityAuthorityPayloadCodecV1
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

    internal static byte[] Encode(VadObservationV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.Session, value.ExpectedAuthority, value.BodyBytes);
    }

    internal static byte[] Encode(ActivityBoundaryFactV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encode(value.Session, value.ExpectedAuthority, value.BodyBytes);
    }

    internal static bool TryDecodeVadObservation(ReadOnlyMemory<byte> encoded, out VadObservationV1? value) =>
        TryDecode(encoded, static (session, authority, body) => new VadObservationV1(session, authority, body), out value);

    internal static bool TryDecodeActivityBoundaryFact(ReadOnlyMemory<byte> encoded, out ActivityBoundaryFactV1? value) =>
        TryDecode(encoded, static (session, authority, body) => new ActivityBoundaryFactV1(session, authority, body), out value);

    internal static Hash256 ComputeHash(VadObservationV1 value) =>
        AuthorityIntegrityHashV1.Compute(VadObservationSchemaId, Major, Minor, Encode(value));

    internal static Hash256 ComputeHash(ActivityBoundaryFactV1 value) =>
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
                VadObservationV1 observation => encoded.Span.SequenceEqual(Encode(observation)),
                ActivityBoundaryFactV1 boundary => encoded.Span.SequenceEqual(Encode(boundary)),
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
