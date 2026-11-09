using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ActivityAuthorityPayloadCodecTests
{
    [Fact]
    public void Registered_activity_payloads_round_trip_canonically_and_own_body_bytes()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var observation = new VadObservation(session, authority, source);
        var boundary = new ActivityBoundaryFact(session, authority, source);
        source[0] = 99;

        var observationBytes = ActivityAuthorityPayloadCodec.Encode(observation);
        var boundaryBytes = ActivityAuthorityPayloadCodec.Encode(boundary);
        Assert.True(ActivityAuthorityPayloadCodec.TryDecodeVadObservation(observationBytes, out var decodedObservation));
        Assert.True(ActivityAuthorityPayloadCodec.TryDecodeActivityBoundaryFact(boundaryBytes, out var decodedBoundary));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedObservation!.Body);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedBoundary!.Body);
        Assert.Equal(observationBytes, ActivityAuthorityPayloadCodec.Encode(decodedObservation));
        Assert.Equal(boundaryBytes, ActivityAuthorityPayloadCodec.Encode(decodedBoundary));
        Assert.NotEqual(ActivityAuthorityPayloadCodec.ComputeHash(observation), ActivityAuthorityPayloadCodec.ComputeHash(boundary));
    }

    [Fact]
    public void Activity_payloads_reject_invalid_authority_and_body_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);

        Assert.Throws<ArgumentException>(() => new VadObservation(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new ActivityBoundaryFact(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Activity_payload_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = ActivityAuthorityPayloadCodec.Encode(new VadObservation(session, authority, [7]));
        var trailing = canonical.Concat(new byte[] { 0 }).ToArray();
        Assert.False(ActivityAuthorityPayloadCodec.TryDecodeVadObservation(trailing, out _));

        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(ActivityAuthorityPayloadCodec.TryDecodeVadObservation(reordered.Encode(), out _));

        Assert.False(ActivityAuthorityPayloadCodec.TryDecodeActivityBoundaryFact(new byte[66_561], out _));
    }

    [Fact]
    public void Activity_payload_registrations_join_the_generated_owner_and_validate_the_outer_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var observation = ActivityAuthorityPayloadCodec.Encode(new VadObservation(session, authority, []));
        var boundary = ActivityAuthorityPayloadCodec.Encode(new ActivityBoundaryFact(session, authority, [8]));

        Assert.Equal((ushort)7, ActivityAuthorityPayloadRegistrations.VadObservationDiscriminator);
        Assert.Equal((ushort)8, ActivityAuthorityPayloadRegistrations.ActivityBoundaryFactDiscriminator);
        Assert.Equal(OwnerSliceId.S3, ActivityAuthorityPayloadRegistrations.VadObservation.Owner);
        Assert.Equal(OwnerSliceId.S3, ActivityAuthorityPayloadRegistrations.ActivityBoundaryFact.Owner);
        Assert.True(ActivityAuthorityPayloadRegistrations.VadObservation.Validate(observation, session));
        Assert.True(ActivityAuthorityPayloadRegistrations.ActivityBoundaryFact.Validate(boundary, session));
        Assert.False(ActivityAuthorityPayloadRegistrations.VadObservation.Validate(observation, other));
        Assert.False(ActivityAuthorityPayloadRegistrations.ActivityBoundaryFact.Validate(boundary, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    {
        var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2));
        return (session, ExpectedAuthorityVectorV1.Create(session, []));
    }

    private static T Id<T>(byte value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Fill(value);
        var stable = StableId128.FromBytes(bytes);
        return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) :
            typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) :
            throw new InvalidOperationException();
    }
}
