using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HPD.Base;

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
    private const int PlaintextLength = 80;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int TokenLength = 1 + NonceLength + PlaintextLength + TagLength;
    private readonly byte[]? _key;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    /// <summary>Initializes a new instance.</summary>
    public BaseRealtimeCursorProtector(
        IOptions<BaseRealtimeOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _key = options.Value.CursorProtectionKey is null
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(
                "HPD.BASE realtime cursor encryption v1\0" + options.Value.CursorProtectionKey));
        _timeProvider = timeProvider;
        _lifetime = TimeSpan.FromSeconds(options.Value.Limits.CursorLifetimeSeconds);
    }

    /// <summary>Gets the enabled.</summary>
    public bool Enabled => _key is not null;

    /// <summary>Executes the protect operation.</summary>
    public string Protect(
        BaseMutationJournalPosition position,
        string storeId,
        BaseRealtimeChannelJoinRequest join)
    {
        if (_key is null)
            throw new InvalidOperationException("Durable realtime cursors are not configured.");
        if (position.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        Span<byte> plaintext = stackalloc byte[PlaintextLength];
        BinaryPrimitives.WriteInt64BigEndian(plaintext[..8], position.Value);
        BinaryPrimitives.WriteInt64BigEndian(
            plaintext[8..16],
            _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        SHA256.HashData(Encoding.UTF8.GetBytes(storeId), plaintext[16..48]);
        ScopeHash(join, plaintext[48..80]);

        Span<byte> token = stackalloc byte[TokenLength];
        token[0] = Version;
        var nonce = token.Slice(1, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = token.Slice(1 + NonceLength, PlaintextLength);
        var tag = token.Slice(1 + NonceLength + PlaintextLength, TagLength);
        using var aes = new AesGcm(_key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, token[..1]);
        CryptographicOperations.ZeroMemory(plaintext);
        return Base64UrlEncode(token);
    }

    /// <summary>Executes the unprotect operation.</summary>
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

        if (token.Length != TokenLength)
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);

        if (token[0] != Version)
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.VersionUnsupported, default);

        Span<byte> plaintext = stackalloc byte[PlaintextLength];
        try
        {
            using var aes = new AesGcm(_key, TagLength);
            aes.Decrypt(
                token.AsSpan(1, NonceLength),
                token.AsSpan(1 + NonceLength, PlaintextLength),
                token.AsSpan(1 + NonceLength + PlaintextLength, TagLength),
                plaintext,
                token.AsSpan(0, 1));
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);
        }

        Span<byte> expectedStore = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(storeId), expectedStore);
        Span<byte> expectedScope = stackalloc byte[32];
        ScopeHash(join, expectedScope);
        if (!CryptographicOperations.FixedTimeEquals(plaintext[16..48], expectedStore)
            || !CryptographicOperations.FixedTimeEquals(plaintext[48..80], expectedScope))
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.ScopeMismatch, default);
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(plaintext[8..16]));
        var age = _timeProvider.GetUtcNow() - issuedAt;
        if (age < TimeSpan.Zero)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Invalid, default);
        }
        if (age > _lifetime)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return new BaseRealtimeCursorReadResult(BaseRealtimeCursorStatus.Expired, default);
        }

        var position = BinaryPrimitives.ReadInt64BigEndian(plaintext[..8]);
        CryptographicOperations.ZeroMemory(plaintext);
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
