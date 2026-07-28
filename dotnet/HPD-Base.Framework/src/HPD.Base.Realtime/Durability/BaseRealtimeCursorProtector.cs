using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Base.Events;
using HPD.Base.Realtime.Configuration;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.Durability;

internal enum BaseRealtimeCursorStatus
{
    Valid,
    Invalid,
    ScopeMismatch,
    Expired,
    VersionUnsupported
}

internal readonly record struct BaseRealtimeCursorReadResult(
    BaseRealtimeCursorStatus Status,
    BaseMutationJournalPosition Position);

internal sealed class BaseRealtimeCursorProtector
{
    private const byte Version = 1;
    private const int PayloadLength = 81;
    private const int SignatureLength = 32;
    private readonly byte[]? _key;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public BaseRealtimeCursorProtector(
        IOptions<BaseRealtimeOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _key = options.Value.CursorSigningKey is null
            ? null
            : Encoding.UTF8.GetBytes(options.Value.CursorSigningKey);
        _timeProvider = timeProvider;
        _lifetime = TimeSpan.FromSeconds(options.Value.Limits.CursorLifetimeSeconds);
    }

    public bool Enabled => _key is not null;

    public string Protect(
        BaseMutationJournalPosition position,
        string storeId,
        BaseRealtimeChannelJoinRequest join)
    {
        if (_key is null)
            throw new InvalidOperationException("Durable realtime cursors are not configured.");
        if (position.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(payload[1..9], position.Value);
        BinaryPrimitives.WriteInt64BigEndian(
            payload[9..17],
            _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        SHA256.HashData(Encoding.UTF8.GetBytes(storeId), payload[17..49]);
        ScopeHash(join, payload[49..81]);

        Span<byte> signature = stackalloc byte[SignatureLength];
        HMACSHA256.HashData(_key, payload, signature);

        Span<byte> token = stackalloc byte[PayloadLength + SignatureLength];
        payload.CopyTo(token);
        signature.CopyTo(token[PayloadLength..]);
        return Base64UrlEncode(token);
    }

    public BaseRealtimeCursorReadResult Unprotect(
        string cursor,
        string storeId,
        BaseRealtimeChannelJoinRequest join)
    {
        if (_key is null || string.IsNullOrWhiteSpace(cursor))
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);

        byte[] token;
        try
        {
            token = Base64UrlDecode(cursor);
        }
        catch (FormatException)
        {
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);
        }

        if (token.Length != PayloadLength + SignatureLength)
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);

        var payload = token.AsSpan(0, PayloadLength);
        var providedSignature = token.AsSpan(PayloadLength, SignatureLength);
        Span<byte> expectedSignature = stackalloc byte[SignatureLength];
        HMACSHA256.HashData(_key, payload, expectedSignature);
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);

        if (payload[0] != Version)
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.VersionUnsupported, default);

        Span<byte> expectedStore = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(storeId), expectedStore);
        Span<byte> expectedScope = stackalloc byte[32];
        ScopeHash(join, expectedScope);
        if (!CryptographicOperations.FixedTimeEquals(payload[17..49], expectedStore)
            || !CryptographicOperations.FixedTimeEquals(payload[49..81], expectedScope))
        {
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.ScopeMismatch, default);
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload[9..17]));
        if (_timeProvider.GetUtcNow() - issuedAt > _lifetime)
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Expired, default);

        var position = BinaryPrimitives.ReadInt64BigEndian(payload[1..9]);
        return position < 0
            ? new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default)
            : new BaseRealtimeCursorReadResult(
                BaseRealtimeCursorStatus.Valid,
                new BaseMutationJournalPosition(position));
    }

    private static void ScopeHash(BaseRealtimeChannelJoinRequest join, Span<byte> destination)
    {
        var builder = new StringBuilder();
        Append(builder, join.Kind);
        Append(builder, join.Private ? "1" : "0");
        Append(builder, join.CollectionId);
        Append(builder, join.RecordId);
        Append(builder, join.TenantId);
        Append(builder, join.IncludeSnapshots ? "1" : "0");
        Append(builder, join.IncludeBefore ? "1" : "0");
        foreach (var operation in (join.Operations ?? []).Order())
            Append(builder, ((int)operation).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var eventType in (join.EventTypes ?? []).Order(StringComparer.Ordinal))
            Append(builder, eventType);
        SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()), destination);
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length)
            .Append(':')
            .Append(value)
            .Append(';');
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid Base64Url length.")
        };
        return Convert.FromBase64String(padded);
    }
}
