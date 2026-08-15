using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticInputAcceptanceCommandV1Tests
{
    [Fact]
    public void Command_round_trips_owns_body_and_hashes_by_schema()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4]; var command = new SemanticInputAcceptanceCommandV1(session, authority, source); source[0] = 99;
        var encoded = SemanticInputAcceptanceCommandV1Codec.Encode(command); Assert.True(SemanticInputAcceptanceCommandV1Codec.TryDecode(encoded, out var decoded));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decoded!.Body); Assert.Equal(encoded, SemanticInputAcceptanceCommandV1Codec.Encode(decoded));
        Assert.Equal(AuthorityIntegrityHashV1.Compute(SemanticInputAcceptanceCommandV1Codec.SchemaId, 1, 0, encoded), SemanticInputAcceptanceCommandV1Codec.ComputeHash(command));
    }
    [Fact]
    public void Command_rejects_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptanceCommandV1(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptanceCommandV1(session, authority, new byte[65_537]));
    }
    [Fact]
    public void Decoder_fails_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = SemanticInputAcceptanceCommandV1Codec.Encode(new SemanticInputAcceptanceCommandV1(session, authority, [7]));
        Assert.False(SemanticInputAcceptanceCommandV1Codec.TryDecode(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority)); reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(SemanticInputAcceptanceCommandV1Codec.TryDecode(reordered.Encode(), out _)); Assert.False(SemanticInputAcceptanceCommandV1Codec.TryDecode(new byte[66_561], out _));
    }
    [Fact]
    public void Registration_joins_discriminator_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var encoded = SemanticInputAcceptanceCommandV1Codec.Encode(new SemanticInputAcceptanceCommandV1(session, authority, []));
        Assert.Equal((ushort)11, SemanticInputAcceptanceCommandPayloadRegistrationV1.Discriminator); Assert.Equal(OwnerSliceId.S1, SemanticInputAcceptanceCommandPayloadRegistrationV1.Command.Owner);
        Assert.True(SemanticInputAcceptanceCommandPayloadRegistrationV1.Command.Validate(encoded, session)); Assert.False(SemanticInputAcceptanceCommandPayloadRegistrationV1.Command.Validate(encoded, other));
    }
    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
