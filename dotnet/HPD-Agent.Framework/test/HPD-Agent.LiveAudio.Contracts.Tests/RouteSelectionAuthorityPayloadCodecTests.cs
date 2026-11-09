using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class RouteSelectionAuthorityPayloadCodecTests
{
    [Fact]
    public void Route_selection_round_trips_canonically_owns_body_and_hashes_by_schema()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var command = new RouteSelectionCommand(session, authority, source);
        source[0] = 99;
        var encoded = RouteSelectionAuthorityPayloadCodec.Encode(command);
        Assert.True(RouteSelectionAuthorityPayloadCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decoded!.Body);
        Assert.Equal(encoded, RouteSelectionAuthorityPayloadCodec.Encode(decoded));
        Assert.Equal(
            AuthorityIntegrityHashV1.Compute(RouteSelectionAuthorityPayloadCodec.SchemaId, 1, 0, encoded),
            RouteSelectionAuthorityPayloadCodec.ComputeHash(command));
    }

    [Fact]
    public void Route_selection_rejects_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new RouteSelectionCommand(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new RouteSelectionCommand(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Route_selection_decoder_fails_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = RouteSelectionAuthorityPayloadCodec.Encode(new RouteSelectionCommand(session, authority, [7]));
        Assert.False(RouteSelectionAuthorityPayloadCodec.TryDecode(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(RouteSelectionAuthorityPayloadCodec.TryDecode(reordered.Encode(), out _));
        Assert.False(RouteSelectionAuthorityPayloadCodec.TryDecode(new byte[66_561], out _));
    }

    [Fact]
    public void Route_selection_registration_joins_discriminator_owner_and_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var encoded = RouteSelectionAuthorityPayloadCodec.Encode(new RouteSelectionCommand(session, authority, []));
        Assert.Equal((ushort)23, RouteSelectionAuthorityPayloadRegistration.Discriminator);
        Assert.Equal(OwnerSliceId.S8, RouteSelectionAuthorityPayloadRegistration.Command.Owner);
        Assert.True(RouteSelectionAuthorityPayloadRegistration.Command.Validate(encoded, session));
        Assert.False(RouteSelectionAuthorityPayloadRegistration.Command.Validate(encoded, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    {
        var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2));
        return (session, ExpectedAuthorityVectorV1.Create(session, []));
    }

    private static T Id<T>(byte value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes);
        return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) :
            typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException();
    }
}
