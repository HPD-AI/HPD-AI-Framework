using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityGenerationInitializationCodecV1Tests
{
    public static TheoryData<AuthorityAxisId, OwnerSliceId> RegisteredInitializations => new()
    {
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
    [MemberData(nameof(RegisteredInitializations))]
    public void Decoder_ExactJoinsAllSparseAxisSchemasAndOwners(AuthorityAxisId axis, OwnerSliceId owner)
    {
        var session = Session();
        var payload = Encode(session, Initial, owner);

        var result = AuthorityGenerationInitializationCodecV1.Decode(
            AuthorityGenerationInitializationCodecV1.SchemaFor(axis), owner, session, payload, out var initialization);

        Assert.Equal(AuthorityGenerationInitializationDecodeV1.Valid, result);
        Assert.Equal((axis, owner, session, Initial),
            (initialization.Axis, initialization.Owner, initialization.Session, initialization.Initial));
        Assert.Equal(owner, AuthorityGenerationInitializationCodecV1.OwnerFor(axis));
    }

    [Fact]
    public void GraphGolden_IsCanonicalAndIndependent()
    {
        const string golden = "a301a20150000102030405060708090a0b0c0d0e0f0250202122232425262728292a2b2c2d2e2f02500102030405060708090a0b0c0d0e0f100302";
        Assert.Equal(golden, Convert.ToHexString(Encode(Session(), Initial, OwnerSliceId.S2)).ToLowerInvariant());
    }

    [Fact]
    public void AllTenSchemas_HaveDistinctExactIntegrityHashDomains()
    {
        var session=Session();
        var rows=new[]{
            (AuthorityAxisId.Graph,OwnerSliceId.S2),(AuthorityAxisId.Activity,OwnerSliceId.S3),
            (AuthorityAxisId.Turn,OwnerSliceId.S4),(AuthorityAxisId.Provider,OwnerSliceId.S5),
            (AuthorityAxisId.Output,OwnerSliceId.S6),(AuthorityAxisId.Sink,OwnerSliceId.S6),
            (AuthorityAxisId.Tool,OwnerSliceId.S7),(AuthorityAxisId.Route,OwnerSliceId.S8),
            (AuthorityAxisId.Privacy,OwnerSliceId.S9),(AuthorityAxisId.Transport,OwnerSliceId.S11)};
        var hashes=rows.Select(row=>
        {
            var encoded=AuthorityGenerationInitializationCodecV1.Encode(session,row.Item1,Initial);
            Assert.Equal(Encode(session,Initial,row.Item2),encoded);
            var expected=AuthorityIntegrityHashV1.Compute(
                AuthorityGenerationInitializationCodecV1.SchemaTokenFor(row.Item1).ToString(),1,0,encoded);
            var actual=AuthorityGenerationInitializationCodecV1.ComputeHash(session,row.Item1,Initial);
            Assert.Equal(expected,actual);
            return actual;
        }).ToArray();
        Assert.Equal(10,hashes.Distinct().Count());
    }

    [Fact]
    public void Decoder_DistinguishesUnknownSchemaFromInvalidKnownInitialization()
    {
        var session = Session();
        var schema = AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph);
        var payload = Encode(session, Initial, OwnerSliceId.S2);

        Assert.Equal(AuthorityGenerationInitializationDecodeV1.NotInitialization,
            AuthorityGenerationInitializationCodecV1.Decode(
                new SchemaReferenceV1(SchemaId.Create(), 1, 0), OwnerSliceId.S2, session, payload, out _));
        Assert.Equal(AuthorityGenerationInitializationDecodeV1.Invalid,
            AuthorityGenerationInitializationCodecV1.Decode(schema, OwnerSliceId.S3, session, payload, out _));
        Assert.Equal(AuthorityGenerationInitializationDecodeV1.Invalid,
            AuthorityGenerationInitializationCodecV1.Decode(schema, OwnerSliceId.S2,
                new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), payload, out _));
        Assert.Equal(AuthorityGenerationInitializationDecodeV1.Invalid,
            AuthorityGenerationInitializationCodecV1.Decode(schema, OwnerSliceId.S2, session,
                Encode(session, Initial, OwnerSliceId.S3), out _));
        Assert.Equal(AuthorityGenerationInitializationDecodeV1.Invalid,
            AuthorityGenerationInitializationCodecV1.Decode(schema, OwnerSliceId.S2, session, new byte[] { 0xff }, out _));
    }

    private static readonly StableId128 Initial = StableId128.FromBytes(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10"));

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));

    private static byte[] Encode(SessionAuthorityStampV1 session, StableId128 initial, OwnerSliceId owner)
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(initial.TryWriteBytes(bytes));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(bytes);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)owner);
        writer.WriteEndMap();
        return writer.Encode();
    }
}
