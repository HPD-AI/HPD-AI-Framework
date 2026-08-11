using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityEnvelopePrimitiveCodecsV1Tests
{
    [Fact]
    public void SchemaReference_RoundTripsExactBounds()
    {
        var expected = new SchemaReferenceV1(SchemaId.Create(), ushort.MaxValue, ushort.MaxValue);
        Assert.True(AuthorityEnvelopePrimitiveCodecsV1.TryDecodeSchemaReference(
            AuthorityEnvelopePrimitiveCodecsV1.Encode(expected), out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SchemaReference_RejectsInvalidMajorAndMalformedWire()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaReferenceV1(SchemaId.Create(), 0, 0));
        Assert.Throws<ArgumentException>(() => AuthorityEnvelopePrimitiveCodecsV1.Encode(default(SchemaReferenceV1)));
        Assert.False(AuthorityEnvelopePrimitiveCodecsV1.TryDecodeSchemaReference(Convert.FromHexString("a30150000102030405060708090a0b0c0d0e0f02000300"), out _));
        Assert.False(AuthorityEnvelopePrimitiveCodecsV1.TryDecodeSchemaReference(Convert.FromHexString("a30150000102030405060708090a0b0c0d0e0f0201030000"), out _));
    }

    [Fact]
    public void IntegrityEnvelope_OwnsSignatureAndRoundTripsMaximumBounds()
    {
        var source = Enumerable.Repeat((byte)0x5a, 4096).ToArray();
        Assert.True(Hash256.TryParse(new string('a', 64), out var digest));
        var expected = new IntegrityEnvelopeV1(ushort.MaxValue, uint.MaxValue, digest, source);
        source[0] = 0;

        Assert.Equal(0x5a, expected.Signature[0]);
        Assert.True(AuthorityEnvelopePrimitiveCodecsV1.TryDecodeIntegrityEnvelope(
            AuthorityEnvelopePrimitiveCodecsV1.Encode(expected), out var actual));
        Assert.NotNull(actual);
        Assert.Equal(expected.Profile, actual.Profile);
        Assert.Equal(expected.KeyVersion, actual.KeyVersion);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.Signature, actual.Signature);
    }

    [Fact]
    public void IntegrityEnvelope_RejectsInvalidScalarsAndOversizeSignature()
    {
        Assert.True(Hash256.TryParse(new string('b', 64), out var digest));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntegrityEnvelopeV1(0, 1, digest, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntegrityEnvelopeV1(1, 0, digest, []));
        Assert.Throws<ArgumentException>(() => new IntegrityEnvelopeV1(1, 1, default, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntegrityEnvelopeV1(1, 1, digest, new byte[4097]));
        Assert.False(AuthorityEnvelopePrimitiveCodecsV1.TryDecodeIntegrityEnvelope(Array.Empty<byte>(), out _));
    }
}
