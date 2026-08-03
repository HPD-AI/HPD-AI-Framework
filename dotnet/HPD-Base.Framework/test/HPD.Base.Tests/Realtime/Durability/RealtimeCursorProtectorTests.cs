using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Realtime.Durability;

public sealed class RealtimeCursorProtectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTripsOnlyForTheBoundStoreAndChannelScope()
    {
        var time = new ManualTimeProvider(Now);
        var protector = Create(time);
        var join = Join();
        var cursor = protector.Protect(new BaseMutationJournalPosition(42), 7, "private-store-marker", join);

        var valid = protector.Unprotect(cursor, 7, "private-store-marker", join);
        var wrongStore = protector.Unprotect(cursor, 7, "other-store", join);
        var wrongTenant = protector.Unprotect(cursor, 7, "private-store-marker", join with { TenantId = "tenant-b" });
        var restored = protector.Unprotect(cursor, 8, "private-store-marker", join);

        valid.Status.Should().Be(BaseRealtimeCursorStatus.Valid);
        valid.Position.Value.Should().Be(42);
        wrongStore.Status.Should().Be(BaseRealtimeCursorStatus.ScopeMismatch);
        wrongTenant.Status.Should().Be(BaseRealtimeCursorStatus.ScopeMismatch);
        restored.Status.Should().Be(BaseRealtimeCursorStatus.RestoreInvalidated);
        cursor.Should().NotContain("private-store-marker");
        cursor.Should().NotContain("tenant-a");
        cursor.Should().NotContain("items");
    }

    [Fact]
    public void DecodedWireTokenDoesNotContainRecoverableCursorMetadata()
    {
        var protector = Create(new ManualTimeProvider(Now));
        var cursor = protector.Protect(
            new BaseMutationJournalPosition(0x0102030405060708),
            7,
            "private-store-marker",
            Join());
        var token = Decode(cursor);
        Span<byte> position = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(position, 0x0102030405060708);
        Span<byte> timestamp = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(timestamp, Now.ToUnixTimeSeconds());
        var storeHash = SHA256.HashData(Encoding.UTF8.GetBytes("private-store-marker"));

        ContainsSequence(token, position).Should().BeFalse();
        ContainsSequence(token, timestamp).Should().BeFalse();
        ContainsSequence(token, storeHash).Should().BeFalse();
        protector.Protect(
                new BaseMutationJournalPosition(0x0102030405060708),
                7,
                "private-store-marker",
                Join())
            .Should().NotBe(cursor);
    }

    [Fact]
    public void DistinguishesMalformedExpiredAndUnsupportedVersionTokens()
    {
        var time = new ManualTimeProvider(Now);
        var protector = Create(time, lifetimeSeconds: 5);
        var join = Join();
        var cursor = protector.Protect(new BaseMutationJournalPosition(7), 0, "store", join);

        protector.Unprotect("not_base64!", 0, "store", join).Status
            .Should().Be(BaseRealtimeCursorStatus.Invalid);

        time.Advance(TimeSpan.FromSeconds(6));
        protector.Unprotect(cursor, 0, "store", join).Status
            .Should().Be(BaseRealtimeCursorStatus.Expired);

        var unsupported = WithVersion(cursor, 3);
        protector.Unprotect(unsupported, 0, "store", join).Status
            .Should().Be(BaseRealtimeCursorStatus.VersionUnsupported);
    }

    private static BaseRealtimeCursorProtector Create(
        TimeProvider timeProvider,
        int lifetimeSeconds = 60) =>
        new(
            new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
            {
                ActiveKey = new BaseOpaqueTokenKey { Id = 3, Key = Enumerable.Repeat((byte)0x55, 32).ToArray() },
            })),
            Options.Create(new BaseRealtimeOptions
            {
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
        token[1] = version;
        return Encode(token);
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.Slice(index, needle.Length).SequenceEqual(needle))
                return true;
        }

        return false;
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
