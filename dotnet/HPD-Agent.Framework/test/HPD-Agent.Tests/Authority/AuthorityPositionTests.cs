using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityPositionTests
{
    private static readonly StableId128 First = StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));
    private static readonly StableId128 Second = StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"));

    [Fact]
    public void JournalPosition_HasDeterministicNestedEncodingAndRoundTrips()
    {
        var stamp = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(First), LiveSessionId.FromValue(Second));
        var position = new JournalPositionV1(stamp, 1);

        var encoded = AuthorityPositionCodecsV1.Encode(position);

        Assert.Equal("a201a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f0201", Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.True(AuthorityPositionCodecsV1.TryDecodeJournal(encoded, out var decoded));
        Assert.Equal(position, decoded);
    }

    [Fact]
    public void ThreadPosition_HasDeterministicEncodingAndRoundTrips()
    {
        var position = new ThreadPositionV1(ThreadId.FromValue(First), 2, 3);

        var encoded = AuthorityPositionCodecsV1.Encode(position);

        Assert.Equal("a30150000102030405060708090a0b0c0d0e0f02020303", Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.True(AuthorityPositionCodecsV1.TryDecodeThread(encoded, out var decoded));
        Assert.Equal(position, decoded);
    }

    [Fact]
    public void PositionConstructors_RejectDefaultsAndNonpositiveCounters()
    {
        var stamp = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(First), LiveSessionId.FromValue(Second));
        var thread = ThreadId.FromValue(First);

        Assert.Throws<ArgumentException>(() => new JournalPositionV1(default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JournalPositionV1(stamp, 0));
        Assert.Throws<ArgumentException>(() => new ThreadPositionV1(default, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThreadPositionV1(thread, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThreadPositionV1(thread, 1, 0));
    }

    [Theory]
    [InlineData("a201a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f0200")]
    [InlineData("a30150000102030405060708090a0b0c0d0e0f02000301")]
    [InlineData("a30150000102030405060708090a0b0c0d0e0f02010300")]
    public void Decoders_RejectNonpositivePositions(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.False(AuthorityPositionCodecsV1.TryDecodeJournal(bytes, out _));
        Assert.False(AuthorityPositionCodecsV1.TryDecodeThread(bytes, out _));
    }
}
