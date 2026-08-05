using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal enum BaseRealtimeCursorStatus { Valid, Invalid, ScopeMismatch, Expired, VersionUnsupported, RestoreInvalidated }
internal readonly record struct BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus Status, BaseMutationJournalPosition Position);

internal sealed class BaseRealtimeCursorProtector(
    BaseOpaqueTokenProtector tokens,
    IOptions<BaseRealtimeOptions> options,
    TimeProvider timeProvider)
{
    private const byte Version = 2;
    private const string Purpose = "hpd.base.realtime.cursor";
    private const int PlaintextLength = 56;
    private readonly TimeSpan _lifetime = TimeSpan.FromSeconds(options.Value.Limits.CursorLifetimeSeconds);

    public bool Enabled => true;

    public string Protect(BaseMutationJournalPosition position, long restoreEpoch, string storeId, BaseRealtimeChannelJoinRequest join)
    {
        if (position.Value < 0) throw new ArgumentOutOfRangeException(nameof(position));
        if (restoreEpoch < 0) throw new ArgumentOutOfRangeException(nameof(restoreEpoch));
        Span<byte> plaintext = stackalloc byte[PlaintextLength];
        BinaryPrimitives.WriteInt64BigEndian(plaintext[..8], position.Value);
        BinaryPrimitives.WriteInt64BigEndian(plaintext[8..], timeProvider.GetUtcNow().ToUnixTimeSeconds());
        BinaryPrimitives.WriteInt64BigEndian(plaintext[16..], restoreEpoch);
        Span<byte> scope = stackalloc byte[32];
        ScopeHash(storeId, join, scope);
        scope.CopyTo(plaintext[24..]);
        Span<byte> binding = stackalloc byte[32];
        return tokens.Protect(Purpose, Version, plaintext, binding);
    }

    public BaseRealtimeCursorReadResult Unprotect(string cursor, long restoreEpoch, string storeId, BaseRealtimeChannelJoinRequest join)
    {
        Span<byte> scope = stackalloc byte[32];
        ScopeHash(storeId, join, scope);
        Span<byte> binding = stackalloc byte[32];
        BaseOpaqueTokenResult result = tokens.Unprotect(Purpose, Version, cursor, PlaintextLength, binding);
        if (result.Status == BaseOpaqueTokenStatus.VersionUnsupported) return new(BaseRealtimeCursorStatus.VersionUnsupported, default);
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return new(BaseRealtimeCursorStatus.Invalid, default);
        byte[] plaintext = result.Plaintext;
        try
        {
            long position = BinaryPrimitives.ReadInt64BigEndian(plaintext.AsSpan(0, 8));
            if (!CryptographicOperations.FixedTimeEquals(plaintext.AsSpan(24, 32), scope))
                return new(BaseRealtimeCursorStatus.ScopeMismatch, default);
            long tokenRestoreEpoch = BinaryPrimitives.ReadInt64BigEndian(plaintext.AsSpan(16, 8));
            if (tokenRestoreEpoch != restoreEpoch)
                return new(BaseRealtimeCursorStatus.RestoreInvalidated, default);
            DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(plaintext.AsSpan(8, 8)));
            TimeSpan age = timeProvider.GetUtcNow() - issuedAt;
            if (position < 0 || age < TimeSpan.Zero) return new(BaseRealtimeCursorStatus.Invalid, default);
            if (age > _lifetime) return new(BaseRealtimeCursorStatus.Expired, default);
            return new(BaseRealtimeCursorStatus.Valid, new BaseMutationJournalPosition(position));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static void ScopeHash(string storeId, BaseRealtimeChannelJoinRequest join, Span<byte> destination)
    {
        var builder = new StringBuilder(storeId);
        Append(builder, join.Kind); Append(builder, join.Private ? "1" : "0"); Append(builder, join.CollectionId);
        Append(builder, join.RecordId); Append(builder, join.TenantId); Append(builder, join.IncludeSnapshots ? "1" : "0"); Append(builder, join.IncludeBefore ? "1" : "0");
        foreach (BaseOperationKind operation in (join.Operations ?? []).Order()) Append(builder, ((int)operation).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (string eventType in (join.EventTypes ?? []).Order(StringComparer.Ordinal)) Append(builder, eventType);
        SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()), destination);
    }
    private static void Append(StringBuilder builder, string? value) { value ??= string.Empty; builder.Append(value.Length).Append(':').Append(value).Append(';'); }
}
