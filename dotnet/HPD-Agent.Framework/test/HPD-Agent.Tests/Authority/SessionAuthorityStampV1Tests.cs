using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionAuthorityStampV1Tests
{
    private static readonly byte[] RuntimeBytes = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    private static readonly byte[] SessionBytes = Convert.FromHexString("101112131415161718191a1b1c1d1e1f");

    [Fact]
    public void CanonicalEncodingAndIntegrityHash_MatchCheckedInGolden()
    {
        var stamp = CreateStamp();

        var encoded = SessionAuthorityStampV1Codec.Encode(stamp);
        var hash = SessionAuthorityStampV1Codec.ComputeIntegrityHash(stamp);

        Assert.Equal("a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f", Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.Equal("429698042c849d1302b7b44b7e16ee44d3a25d25ad58a548f30c7c472f1f304e", hash.ToString());
    }

    [Fact]
    public void CanonicalEncoding_RoundTripsExactSemanticTypes()
    {
        var stamp = CreateStamp();

        Assert.True(SessionAuthorityStampV1Codec.TryDecode(SessionAuthorityStampV1Codec.Encode(stamp), out var decoded));
        Assert.Equal(stamp, decoded);
    }

    [Theory]
    [InlineData("bf0150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1fff")]
    [InlineData("a20250101112131415161718191a1b1c1d1e1f0150000102030405060708090a0b0c0d0e0f")]
    [InlineData("a30150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f0350000102030405060708090a0b0c0d0e0f")]
    [InlineData("a2014f000102030405060708090a0b0c0d0e0250101112131415161718191a1b1c1d1e1f")]
    public void Decoder_RejectsNoncanonicalUnknownOrMalformedInput(string hex) =>
        Assert.False(SessionAuthorityStampV1Codec.TryDecode(Convert.FromHexString(hex), out _));

    [Fact]
    public void PublicConstructor_RejectsDefaultIdentifiers()
    {
        var runtime = RuntimeGenerationId.FromValue(StableId128.FromBytes(RuntimeBytes));
        var session = LiveSessionId.FromValue(StableId128.FromBytes(SessionBytes));

        Assert.Throws<ArgumentException>(() => new SessionAuthorityStampV1(default, session));
        Assert.Throws<ArgumentException>(() => new SessionAuthorityStampV1(runtime, default));
        Assert.False(default(SessionAuthorityStampV1).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HPD.SESSION-AUTHORITY-STAMP.V1")]
    [InlineData("hpd.session\0authority.v1")]
    [InlineData("hpd.sessiön-authority-stamp.v1")]
    [InlineData("hpd.unknown-schema.v1")]
    public void IntegrityHash_RejectsMalformedOrUnregisteredSchemaIds(string schemaId) =>
        Assert.Throws<ArgumentException>(() => AuthorityIntegrityHashV1.Compute(schemaId, 1, 0, []));

    [Fact]
    public void IntegrityHash_RejectsInvalidUtf16SchemaId() =>
        Assert.Throws<ArgumentException>(() => AuthorityIntegrityHashV1.Compute("hpd.\ud800.v1", 1, 0, []));

    private static SessionAuthorityStampV1 CreateStamp() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(RuntimeBytes)),
        LiveSessionId.FromValue(StableId128.FromBytes(SessionBytes)));
}
