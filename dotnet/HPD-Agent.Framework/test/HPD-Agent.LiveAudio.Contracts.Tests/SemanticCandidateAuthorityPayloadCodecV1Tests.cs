using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class SemanticCandidateAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Semantic_candidate_round_trips_owns_body_and_hashes_by_schema()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4]; var candidate = new SemanticCandidateV1(session, authority, source); source[0] = 99;
        var encoded = SemanticCandidateAuthorityPayloadCodecV1.Encode(candidate);
        Assert.True(SemanticCandidateAuthorityPayloadCodecV1.TryDecode(encoded, out var decoded));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decoded!.Body); Assert.Equal(encoded, SemanticCandidateAuthorityPayloadCodecV1.Encode(decoded));
        Assert.Equal(AuthorityIntegrityHashV1.Compute(SemanticCandidateAuthorityPayloadCodecV1.SchemaId, 1, 0, encoded), SemanticCandidateAuthorityPayloadCodecV1.ComputeHash(candidate));
    }
    [Fact]
    public void Semantic_candidate_rejects_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new SemanticCandidateV1(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new SemanticCandidateV1(session, authority, new byte[65_537]));
    }
    [Fact]
    public void Semantic_candidate_decoder_fails_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = SemanticCandidateAuthorityPayloadCodecV1.Encode(new SemanticCandidateV1(session, authority, [7]));
        Assert.False(SemanticCandidateAuthorityPayloadCodecV1.TryDecode(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority)); reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(SemanticCandidateAuthorityPayloadCodecV1.TryDecode(reordered.Encode(), out _)); Assert.False(SemanticCandidateAuthorityPayloadCodecV1.TryDecode(new byte[66_561], out _));
    }
    [Fact]
    public void Semantic_candidate_registration_joins_discriminator_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var encoded = SemanticCandidateAuthorityPayloadCodecV1.Encode(new SemanticCandidateV1(session, authority, []));
        Assert.Equal((ushort)9, SemanticCandidateAuthorityPayloadRegistrationV1.Discriminator); Assert.Equal(OwnerSliceId.S4, SemanticCandidateAuthorityPayloadRegistrationV1.Candidate.Owner);
        Assert.True(SemanticCandidateAuthorityPayloadRegistrationV1.Candidate.Validate(encoded, session)); Assert.False(SemanticCandidateAuthorityPayloadRegistrationV1.Candidate.Validate(encoded, other));
    }
    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
