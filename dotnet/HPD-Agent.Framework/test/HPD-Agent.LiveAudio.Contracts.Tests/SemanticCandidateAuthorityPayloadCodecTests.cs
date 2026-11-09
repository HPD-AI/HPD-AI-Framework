using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class SemanticCandidateAuthorityPayloadCodecTests
{
    [Fact]
    public void Semantic_candidate_round_trips_owns_body_and_hashes_by_schema()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4]; var candidate = new SemanticCandidate(session, authority, source); source[0] = 99;
        var encoded = SemanticCandidateAuthorityPayloadCodec.Encode(candidate);
        Assert.True(SemanticCandidateAuthorityPayloadCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decoded!.Body); Assert.Equal(encoded, SemanticCandidateAuthorityPayloadCodec.Encode(decoded));
        Assert.Equal(AuthorityIntegrityHashV1.Compute(SemanticCandidateAuthorityPayloadCodec.SchemaId, 1, 0, encoded), SemanticCandidateAuthorityPayloadCodec.ComputeHash(candidate));
    }
    [Fact]
    public void Semantic_candidate_rejects_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new SemanticCandidate(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new SemanticCandidate(session, authority, new byte[65_537]));
    }
    [Fact]
    public void Semantic_candidate_decoder_fails_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = SemanticCandidateAuthorityPayloadCodec.Encode(new SemanticCandidate(session, authority, [7]));
        Assert.False(SemanticCandidateAuthorityPayloadCodec.TryDecode(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority)); reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(SemanticCandidateAuthorityPayloadCodec.TryDecode(reordered.Encode(), out _)); Assert.False(SemanticCandidateAuthorityPayloadCodec.TryDecode(new byte[66_561], out _));
    }
    [Fact]
    public void Semantic_candidate_registration_joins_discriminator_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var encoded = SemanticCandidateAuthorityPayloadCodec.Encode(new SemanticCandidate(session, authority, []));
        Assert.Equal((ushort)9, SemanticCandidateAuthorityPayloadRegistration.Discriminator); Assert.Equal(OwnerSliceId.S4, SemanticCandidateAuthorityPayloadRegistration.Candidate.Owner);
        Assert.True(SemanticCandidateAuthorityPayloadRegistration.Candidate.Validate(encoded, session)); Assert.False(SemanticCandidateAuthorityPayloadRegistration.Candidate.Validate(encoded, other));
    }
    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
