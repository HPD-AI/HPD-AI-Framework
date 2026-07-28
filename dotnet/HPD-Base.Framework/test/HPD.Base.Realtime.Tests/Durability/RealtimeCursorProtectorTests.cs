using System.Security.Cryptography;
using System.Text;
using HPD.Base.Events;
using HPD.Base.Realtime.Configuration;
using HPD.Base.Realtime.Durability;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.Tests.Durability;

public sealed class RealtimeCursorProtectorTests
{
    private const string SigningKey = "test-only-cursor-signing-key-32-bytes-minimum";
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTripsOnlyForTheBoundStoreAndChannelScope()
    {
        var time = new ManualTimeProvider(Now);
        var protector = Create(time);
        var join = Join();
        var cursor = protector.Protect(new BaseMutationJournalPosition(42), "private-store-marker", join);

        var valid = protector.Unprotect(cursor, "private-store-marker", join);
        var wrongStore = protector.Unprotect(cursor, "other-store", join);
        var wrongTenant = protector.Unprotect(cursor, "private-store-marker", join with { TenantId = "tenant-b" });

        valid.Status.Should().Be(BaseRealtimeCursorStatus.Valid);
        valid.Position.Value.Should().Be(42);
        wrongStore.Status.Should().Be(BaseRealtimeCursorStatus.ScopeMismatch);
        wrongTenant.Status.Should().Be(BaseRealtimeCursorStatus.ScopeMismatch);
        cursor.Should().NotContain("private-store-marker");
        cursor.Should().NotContain("tenant-a");
        cursor.Should().NotContain("items");
        cursor.Should().NotContain("42");
    }

    [Fact]
    public void DistinguishesMalformedExpiredAndUnsupportedVersionTokens()
    {
        var time = new ManualTimeProvider(Now);
        var protector = Create(time, lifetimeSeconds: 5);
        var join = Join();
        var cursor = protector.Protect(new BaseMutationJournalPosition(7), "store", join);

        protector.Unprotect("not_base64!", "store", join).Status
            .Should().Be(BaseRealtimeCursorStatus.Invalid);

        time.Advance(TimeSpan.FromSeconds(6));
        protector.Unprotect(cursor, "store", join).Status
            .Should().Be(BaseRealtimeCursorStatus.Expired);

        var unsupported = WithVersion(cursor, 2);
        protector.Unprotect(unsupported, "store", join).Status
            .Should().Be(BaseRealtimeCursorStatus.VersionUnsupported);
    }

    private static BaseRealtimeCursorProtector Create(
        TimeProvider timeProvider,
        int lifetimeSeconds = 60) =>
        new(
            Options.Create(new BaseRealtimeOptions
            {
                CursorSigningKey = SigningKey,
                Limits = new BaseRealtimeLimits
                {
                    CursorLifetimeSeconds = lifetimeSeconds
                }
            }),
            timeProvider);

    private static BaseRealtimeChannelJoinRequest Join() => new()
    {
        Kind = BaseRealtimeChannelKinds.RecordChanges,
        CollectionId = "items",
        TenantId = "tenant-a",
        Durable = true,
        IncludeSnapshots = true
    };

    private static string WithVersion(string cursor, byte version)
    {
        var token = Decode(cursor);
        token[0] = version;
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningKey),
            token.AsSpan(0, 81));
        signature.CopyTo(token, 81);
        return Encode(token);
    }

    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException()
        };
        return Convert.FromBase64String(padded);
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan value) => utcNow += value;
    }
}
