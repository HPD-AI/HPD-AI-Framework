using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticInputAcceptedOuterV1Tests
{
    [Fact]
    public void Outer_round_trips_owns_body_and_hashes_in_outer_domain()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3];
        var outer = new SemanticInputAcceptedOuterV1(session, authority, source); source[0] = 99;
        var encoded = SemanticInputAcceptedOuterCodecV1.Encode(outer);
        Assert.True(SemanticInputAcceptedOuterCodecV1.TryDecode(encoded, out var decoded));
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded!.Body);
        Assert.Equal(encoded, SemanticInputAcceptedOuterCodecV1.Encode(decoded));
        Assert.Equal(AuthorityIntegrityHashV1.Compute(SemanticInputAcceptedOuterCodecV1.SchemaId, 1, 0, encoded),
            SemanticInputAcceptedOuterCodecV1.ComputeHash(outer));
    }

    [Fact]
    public void Outer_rejects_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(3)), LiveSessionId.FromValue(Id(4)));
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptedOuterV1(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptedOuterV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Outer_decoder_rejects_noncanonical_trailing_and_oversize_values()
    {
        var (session, authority) = Authority();
        var canonical = SemanticInputAcceptedOuterCodecV1.Encode(new SemanticInputAcceptedOuterV1(session, authority, [7]));
        Assert.False(SemanticInputAcceptedOuterCodecV1.TryDecode(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(SemanticInputAcceptedOuterCodecV1.TryDecode(reordered.Encode(), out _));
        Assert.False(SemanticInputAcceptedOuterCodecV1.TryDecode(new byte[66_561], out _));
    }

    [Fact]
    public void Registration_binds_discriminator_owner_and_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(3)), LiveSessionId.FromValue(Id(4)));
        var encoded = SemanticInputAcceptedOuterCodecV1.Encode(new SemanticInputAcceptedOuterV1(session, authority, []));
        Assert.Equal((ushort)12, SemanticInputAcceptedOuterPayloadRegistrationV1.Discriminator);
        Assert.Equal(OwnerSliceId.S1, SemanticInputAcceptedOuterPayloadRegistrationV1.Accepted.Owner);
        Assert.True(SemanticInputAcceptedOuterPayloadRegistrationV1.Accepted.Validate(encoded, session));
        Assert.False(SemanticInputAcceptedOuterPayloadRegistrationV1.Accepted.Validate(encoded, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2))); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static StableId128 Id(byte value)
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); return StableId128.FromBytes(bytes); }
}
