using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticInputAcceptedV1Tests
{
    [Fact]
    public void CanonicalEncodingAndHash_MatchIndependentGolden()
    {
        var value = Create();
        const string encoded = "a40150000102030405060708090a0b0c0d0e0f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f020303a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f02800401";
        Assert.Equal(encoded, Convert.ToHexString(SemanticInputAcceptedV1Codec.Encode(value)).ToLowerInvariant());
        Assert.Equal("63f7ad125390a51f8a22f2c6918f55887546bfd632c9ce3bf4480995580469c6",
            SemanticInputAcceptedV1Codec.ComputeIntegrityHash(value).ToString());
    }

    [Fact]
    public void CanonicalEncoding_RoundTripsExactSemanticTypes()
    {
        var expected = Create();
        Assert.True(SemanticInputAcceptedV1Codec.TryDecode(SemanticInputAcceptedV1Codec.Encode(expected), out var actual));
        Assert.Equal(expected, actual);
        Assert.True(expected == actual);
        Assert.False(expected != actual);
        Assert.True((SemanticInputAcceptedV1?)null == null);
        Assert.False(expected == null);
    }

    [Theory]
    [InlineData("a0")]
    [InlineData("bf0100ff")]
    [InlineData("a40150000102030405060708090a0b0c0d0e0f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f020303a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f02800402")]
    public void Decoder_RejectsMalformedNoncanonicalOrUnknownDisposition(string hex) =>
        Assert.False(SemanticInputAcceptedV1Codec.TryDecode(Convert.FromHexString(hex), out _));

    [Fact]
    public void Constructor_RejectsSessionMismatchAndDefaultValues()
    {
        var value = Create();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptedV1(
            value.OperationId, value.SourcePosition, ExpectedAuthorityVectorV1.Create(other, []), value.Disposition));
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptedV1(
            default, value.SourcePosition, value.Authority, value.Disposition));
        Assert.Throws<ArgumentNullException>(() => new SemanticInputAcceptedV1(
            value.OperationId, value.SourcePosition, null!, value.Disposition));
        Assert.Throws<ArgumentException>(() => new SemanticInputAcceptedV1(
            value.OperationId, value.SourcePosition, value.Authority, (SemanticInputAcceptanceDispositionV1)2));
    }

    [Fact]
    public void ExactAdmissionRegistration_UsesAgentCoreSchemaOwnerAndCodec()
    {
        var registration = new SemanticInputAcceptedPayloadRegistrationV1();
        var encoded = SemanticInputAcceptedV1Codec.Encode(Create());
        Assert.Equal(OwnerSliceId.AgentCore, registration.Owner);
        Assert.Equal("sch:2F7DTFG4X6TVJ4A46YAV5WMHP0", registration.Schema.SchemaId.ToString());
        Assert.True(registration.Validate(encoded));
        Assert.False(registration.Validate(new byte[] { 0xff }));
    }

    private static SemanticInputAcceptedV1 Create()
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"))),
            LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));
        return new SemanticInputAcceptedV1(
            OperationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
            new JournalPositionV1(session, 3), ExpectedAuthorityVectorV1.Create(session, []),
            SemanticInputAcceptanceDispositionV1.Accepted);
    }
}
