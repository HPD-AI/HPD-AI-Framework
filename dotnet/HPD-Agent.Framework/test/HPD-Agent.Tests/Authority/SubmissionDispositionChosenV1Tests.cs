using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SubmissionDispositionChosenV1Tests
{
    private const string Golden = "a40150000102030405060708090a0b0c0d0e0f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f020303a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f02800401";

    [Fact]
    public void ClaimedEncodingAndHash_MatchIndependentGolden()
    {
        var value = Create(SubmissionDispositionV1.SubmissionClaimed);
        Assert.Equal(Golden, Convert.ToHexString(SubmissionDispositionChosenV1Codec.Encode(value)).ToLowerInvariant());
        Assert.Equal("7ef327c0d21de8a037bedfdd7d86b1b9f1b161b062d6eb388ab65ecfad554f5d",
            SubmissionDispositionChosenV1Codec.ComputeIntegrityHash(value).ToString());
    }

    [Theory]
    [InlineData(SubmissionDispositionV1.SubmissionClaimed)]
    [InlineData(SubmissionDispositionV1.WithdrawalTombstoned)]
    [InlineData(SubmissionDispositionV1.ReservationConflict)]
    public void ClosedDispositions_RoundTripStructurally(SubmissionDispositionV1 disposition)
    {
        var expected = Create(disposition);
        Assert.True(SubmissionDispositionChosenV1Codec.TryDecode(SubmissionDispositionChosenV1Codec.Encode(expected), out var actual));
        Assert.True(expected == actual);
        Assert.False(expected != actual);
        Assert.True((SubmissionDispositionChosenV1?)null == null);
    }

    [Theory]
    [InlineData("a0")]
    [InlineData("bf0100ff")]
    [InlineData("a40150000102030405060708090a0b0c0d0e0f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f020303a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f02800400")]
    [InlineData("a40150000102030405060708090a0b0c0d0e0f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f020303a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f02800404")]
    public void Decoder_RejectsMalformedNoncanonicalAndUnknownDispositions(string hex) =>
        Assert.False(SubmissionDispositionChosenV1Codec.TryDecode(Convert.FromHexString(hex), out _));

    [Fact]
    public void Constructor_RejectsInvalidInputsAndSessionMismatch()
    {
        var value = Create(SubmissionDispositionV1.SubmissionClaimed);
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.Throws<ArgumentException>(() => new SubmissionDispositionChosenV1(
            default, value.SourcePosition, value.Authority, value.Disposition));
        Assert.Throws<ArgumentNullException>(() => new SubmissionDispositionChosenV1(
            value.OperationId, value.SourcePosition, null!, value.Disposition));
        Assert.Throws<ArgumentException>(() => new SubmissionDispositionChosenV1(
            value.OperationId, value.SourcePosition, ExpectedAuthorityVectorV1.Create(other, []), value.Disposition));
        Assert.Throws<ArgumentException>(() => new SubmissionDispositionChosenV1(
            value.OperationId, value.SourcePosition, value.Authority, (SubmissionDispositionV1)4));
    }

    [Fact]
    public void Registration_UsesExactS1SchemaOwnerAndStrictCodec()
    {
        var registration = new SubmissionDispositionChosenPayloadRegistrationV1();
        Assert.Equal(OwnerSliceId.S1, registration.Owner);
        Assert.Equal("sch:6BJ59Q1XW2RH1W0GBN6EZ423ZB", registration.Schema.SchemaId.ToString());
        Assert.True(registration.Validate(SubmissionDispositionChosenV1Codec.Encode(Create(SubmissionDispositionV1.SubmissionClaimed))));
        Assert.False(registration.Validate(new byte[] { 0xff }));
    }

    private static SubmissionDispositionChosenV1 Create(SubmissionDispositionV1 disposition)
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"))),
            LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));
        return new SubmissionDispositionChosenV1(
            OperationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
            new JournalPositionV1(session, 3), ExpectedAuthorityVectorV1.Create(session, []), disposition);
    }
}
