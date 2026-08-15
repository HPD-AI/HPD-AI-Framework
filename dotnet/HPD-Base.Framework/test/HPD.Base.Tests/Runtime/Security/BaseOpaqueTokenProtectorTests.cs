using Microsoft.Extensions.Options;
using HPD.Base.Testing;

namespace HPD.Base.Tests.Runtime.Security;

public sealed class BaseOpaqueTokenProtectorTests
{
    [Fact]
    public void RetainedKeyReadsOldTokenWhileNewTokensUseActiveKey()
    {
        BaseOpaqueTokenKey oldKey = Key(3, 0x33);
        using var oldProtector = Create(oldKey);
        string token = oldProtector.Protect("query", 1, [1, 2, 3], new byte[32]);
        using var rotated = Create(Key(4, 0x44), oldKey);

        BaseOpaqueTokenResult result = rotated.Unprotect("query", 1, token, 3, new byte[32]);
        string newToken = rotated.Protect("query", 1, [4, 5, 6], new byte[32]);

        result.Status.Should().Be(BaseOpaqueTokenStatus.Valid);
        result.Plaintext.Should().Equal(1, 2, 3);
        Decode(newToken)[0].Should().Be(4);
    }

    [Fact]
    public void PurposeBindingPreventsCrossDecodeAndUnknownKeysAreClassified()
    {
        using var issuer = Create(Key(7, 0x77));
        string token = issuer.Protect("realtime", 2, [9], new byte[32]);
        using var otherRing = Create(Key(8, 0x88));

        issuer.Unprotect("query", 2, token, 1, new byte[32]).Status
            .Should().Be(BaseOpaqueTokenStatus.Invalid);
        otherRing.Unprotect("realtime", 2, token, 1, new byte[32]).Status
            .Should().Be(BaseOpaqueTokenStatus.KeyUnavailable);
    }

    [Fact]
    public void DuplicateOrMalformedKeysAreRejected()
    {
        Action duplicate = () => Create(Key(1, 0x11), Key(1, 0x22));
        Action malformed = () => Create(new BaseOpaqueTokenKey { Id = 1, Key = new byte[31], IssueNotBefore = DateTimeOffset.UnixEpoch });

        duplicate.Should().Throw<ArgumentException>();
        malformed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RetiredKeyRemainsAvailableUntilItsAbsoluteDecryptBoundary()
    {
        DateTimeOffset rotation = new(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var time = new BaseTestTimeProvider(rotation.AddDays(-1));
        BaseOpaqueTokenKey issuing = Key(3, 0x33);
        using var issuer = CreateAt(issuing, time);
        string token = issuer.Protect("vector", 1, [1], new byte[32]);
        BaseOpaqueTokenKey retained = issuing with
        {
            IssueUntil = rotation,
            DecryptUntil = rotation.AddDays(30)
        };
        using var rotated = CreateAt(Key(4, 0x44), time, retained);

        time.SetUtcNow(rotation.AddDays(29));
        rotated.Unprotect("vector", 1, token, 1, new byte[32]).Status
            .Should().Be(BaseOpaqueTokenStatus.Valid);
        time.SetUtcNow(rotation.AddDays(30));
        rotated.Unprotect("vector", 1, token, 1, new byte[32]).Status
            .Should().Be(BaseOpaqueTokenStatus.KeyUnavailable);
    }

    [Fact]
    public void ActiveKeyCannotIssueOutsideItsConfiguredLifetime()
    {
        DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new BaseTestTimeProvider(now);
        using var protector = CreateAt(Key(1, 0x11) with { IssueUntil = now }, time);

        Action issue = () => protector.Protect("vector", 1, [1], new byte[32]);

        issue.Should().Throw<InvalidOperationException>();
    }

    private static BaseOpaqueTokenProtector Create(
        BaseOpaqueTokenKey active,
        params BaseOpaqueTokenKey[] retained) => CreateAt(active, TimeProvider.System, retained);

    private static BaseOpaqueTokenProtector CreateAt(
        BaseOpaqueTokenKey active,
        TimeProvider timeProvider,
        params BaseOpaqueTokenKey[] retained) =>
        new(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = active,
            DecryptionKeys = retained
        }), timeProvider);

    private static BaseOpaqueTokenKey Key(byte id, byte value) => new()
    {
        Id = id,
        Key = Enumerable.Repeat(value, 32).ToArray(),
        IssueNotBefore = DateTimeOffset.UnixEpoch
    };

    private static byte[] Decode(string value)
    {
        string text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - text.Length % 4) % 4), '='));
    }
}
