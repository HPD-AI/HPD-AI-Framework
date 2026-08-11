using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityGenerationTransitionCodecV1Tests
{
    public static TheoryData<AuthorityAxisId, OwnerSliceId> RegisteredTransitions => new()
    {
        { AuthorityAxisId.Runtime, OwnerSliceId.S1 },
        { AuthorityAxisId.Graph, OwnerSliceId.S2 },
        { AuthorityAxisId.Activity, OwnerSliceId.S3 },
        { AuthorityAxisId.Turn, OwnerSliceId.S4 },
        { AuthorityAxisId.Provider, OwnerSliceId.S5 },
        { AuthorityAxisId.Output, OwnerSliceId.S6 },
        { AuthorityAxisId.Sink, OwnerSliceId.S6 },
        { AuthorityAxisId.Tool, OwnerSliceId.S7 },
        { AuthorityAxisId.Route, OwnerSliceId.S8 },
        { AuthorityAxisId.Privacy, OwnerSliceId.S9 },
        { AuthorityAxisId.Transport, OwnerSliceId.S11 },
    };

    [Theory]
    [MemberData(nameof(RegisteredTransitions))]
    public void Decoder_ExactJoinsAllRegisteredAxisSchemasAndOwners(AuthorityAxisId axis, OwnerSliceId owner)
    {
        var session = Session();
        var payload = Encode(session, Expected, Proposed, owner);

        var result = AuthorityGenerationTransitionCodecV1.Decode(
            AuthorityGenerationTransitionCodecV1.SchemaFor(axis), owner, session, payload, out var transition);

        Assert.Equal(AuthorityGenerationTransitionDecodeV1.Valid, result);
        Assert.Equal((axis, owner, session), (transition.Axis, transition.Owner, transition.Session));
        Assert.Equal(Expected, transition.ExpectedPrevious);
        Assert.Equal(Proposed, transition.ProposedNext);
        Assert.Equal(owner, AuthorityGenerationTransitionCodecV1.OwnerFor(axis));
    }

    [Fact]
    public void RuntimeGolden_IsCanonicalAndIndependent()
    {
        var session = Session();
        const string golden = "a401a20150000102030405060708090a0b0c0d0e0f0250202122232425262728292a2b2c2d2e2f02500102030405060708090a0b0c0d0e0f1003501112131415161718191a1b1c1d1e1f200401";
        Assert.Equal(golden, Convert.ToHexString(Encode(session, Expected, Proposed, OwnerSliceId.S1)).ToLowerInvariant());
    }

    [Fact]
    public void Decoder_DistinguishesUnknownSchemaFromInvalidKnownTransition()
    {
        var session = Session();
        var schema = AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Graph);
        Assert.Equal(AuthorityGenerationTransitionDecodeV1.NotTransition,
            AuthorityGenerationTransitionCodecV1.Decode(
                new SchemaReferenceV1(SchemaId.Create(), 1, 0), OwnerSliceId.S2, session,
                Encode(session, Expected, Proposed, OwnerSliceId.S2), out _));
        Assert.Equal(AuthorityGenerationTransitionDecodeV1.Invalid,
            AuthorityGenerationTransitionCodecV1.Decode(schema, OwnerSliceId.S3, session,
                Encode(session, Expected, Proposed, OwnerSliceId.S2), out _));
        Assert.Equal(AuthorityGenerationTransitionDecodeV1.Invalid,
            AuthorityGenerationTransitionCodecV1.Decode(schema, OwnerSliceId.S2,
                new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
                Encode(session, Expected, Proposed, OwnerSliceId.S2), out _));
        Assert.Equal(AuthorityGenerationTransitionDecodeV1.Invalid,
            AuthorityGenerationTransitionCodecV1.Decode(schema, OwnerSliceId.S2, session,
                Encode(session, Expected, Expected, OwnerSliceId.S2), out _));
        Assert.Equal(AuthorityGenerationTransitionDecodeV1.Invalid,
            AuthorityGenerationTransitionCodecV1.Decode(schema, OwnerSliceId.S2, session, new byte[] { 0xff }, out _));
    }

    private static readonly StableId128 Expected = StableId128.FromBytes(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10"));
    private static readonly StableId128 Proposed = StableId128.FromBytes(Convert.FromHexString("1112131415161718191a1b1c1d1e1f20"));

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));

    private static byte[] Encode(
        SessionAuthorityStampV1 session,
        StableId128 expected,
        StableId128 proposed,
        OwnerSliceId owner)
    {
        Span<byte> expectedBytes = stackalloc byte[16];
        Span<byte> proposedBytes = stackalloc byte[16];
        Assert.True(expected.TryWriteBytes(expectedBytes));
        Assert.True(proposed.TryWriteBytes(proposedBytes));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(expectedBytes);
        writer.WriteUInt64(3); writer.WriteByteString(proposedBytes);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)owner);
        writer.WriteEndMap();
        return writer.Encode();
    }
}
