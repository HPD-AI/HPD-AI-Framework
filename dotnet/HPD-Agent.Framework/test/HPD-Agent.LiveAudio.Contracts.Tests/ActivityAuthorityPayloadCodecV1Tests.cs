using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ActivityAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Registered_activity_payloads_round_trip_canonically_and_own_body_bytes()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var observation = new VadObservationV1(session, authority, source);
        var boundary = new ActivityBoundaryFactV1(session, authority, source);
        source[0] = 99;

        var observationBytes = ActivityAuthorityPayloadCodecV1.Encode(observation);
        var boundaryBytes = ActivityAuthorityPayloadCodecV1.Encode(boundary);
        Assert.True(ActivityAuthorityPayloadCodecV1.TryDecodeVadObservation(observationBytes, out var decodedObservation));
        Assert.True(ActivityAuthorityPayloadCodecV1.TryDecodeActivityBoundaryFact(boundaryBytes, out var decodedBoundary));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedObservation!.Body);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedBoundary!.Body);
        Assert.Equal(observationBytes, ActivityAuthorityPayloadCodecV1.Encode(decodedObservation));
        Assert.Equal(boundaryBytes, ActivityAuthorityPayloadCodecV1.Encode(decodedBoundary));
        Assert.NotEqual(ActivityAuthorityPayloadCodecV1.ComputeHash(observation), ActivityAuthorityPayloadCodecV1.ComputeHash(boundary));
    }

    [Fact]
    public void Activity_payloads_reject_invalid_authority_and_body_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);

        Assert.Throws<ArgumentException>(() => new VadObservationV1(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new ActivityBoundaryFactV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Activity_payload_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = ActivityAuthorityPayloadCodecV1.Encode(new VadObservationV1(session, authority, [7]));
        var trailing = canonical.Concat(new byte[] { 0 }).ToArray();
        Assert.False(ActivityAuthorityPayloadCodecV1.TryDecodeVadObservation(trailing, out _));

        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(ActivityAuthorityPayloadCodecV1.TryDecodeVadObservation(reordered.Encode(), out _));

        Assert.False(ActivityAuthorityPayloadCodecV1.TryDecodeActivityBoundaryFact(new byte[66_561], out _));
    }

    [Fact]
    public void Activity_payload_registrations_join_the_generated_owner_and_validate_the_outer_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var observation = ActivityAuthorityPayloadCodecV1.Encode(new VadObservationV1(session, authority, []));
        var boundary = ActivityAuthorityPayloadCodecV1.Encode(new ActivityBoundaryFactV1(session, authority, [8]));

        Assert.Equal((ushort)7, ActivityAuthorityPayloadRegistrationsV1.VadObservationDiscriminator);
        Assert.Equal((ushort)8, ActivityAuthorityPayloadRegistrationsV1.ActivityBoundaryFactDiscriminator);
        Assert.Equal(OwnerSliceId.S3, ActivityAuthorityPayloadRegistrationsV1.VadObservation.Owner);
        Assert.Equal(OwnerSliceId.S3, ActivityAuthorityPayloadRegistrationsV1.ActivityBoundaryFact.Owner);
        Assert.True(ActivityAuthorityPayloadRegistrationsV1.VadObservation.Validate(observation, session));
        Assert.True(ActivityAuthorityPayloadRegistrationsV1.ActivityBoundaryFact.Validate(boundary, session));
        Assert.False(ActivityAuthorityPayloadRegistrationsV1.VadObservation.Validate(observation, other));
        Assert.False(ActivityAuthorityPayloadRegistrationsV1.ActivityBoundaryFact.Validate(boundary, other));
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
