using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal enum BaseOpaqueTokenStatus { Valid, Invalid, KeyUnavailable, VersionUnsupported }
internal readonly record struct BaseOpaqueTokenResult(BaseOpaqueTokenStatus Status, byte[]? Plaintext);

internal sealed class BaseOpaqueTokenProtector : IDisposable
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte _activeId;
    private readonly Dictionary<byte, KeyState> _keys;
    private readonly TimeProvider _timeProvider;

    public BaseOpaqueTokenProtector(
        IOptions<HPDBaseTokenProtectionOptions> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider ?? TimeProvider.System;
        BaseOpaqueTokenKey active = options.Value.ActiveKey ?? throw new ArgumentException("An active token key is required.", nameof(options));
        _activeId = active.Id;
        _keys = new Dictionary<byte, KeyState>();
        Add(active);
        foreach (BaseOpaqueTokenKey key in options.Value.DecryptionKeys ?? []) Add(key);
    }

    public string Protect(string purpose, byte version, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> scopeDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (plaintext.Length > 65_536) throw new ArgumentOutOfRangeException(nameof(plaintext));
        if (scopeDigest.Length != 32) throw new ArgumentOutOfRangeException(nameof(scopeDigest));
        KeyState active = _keys[_activeId];
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (now < active.IssueNotBefore || active.IssueUntil is { } issueUntil && now >= issueUntil)
            throw new InvalidOperationException("The active token key is outside its issuance lifetime.");
        byte[] key = Derive(active.Key, purpose, version);
        byte[] token = new byte[2 + NonceLength + plaintext.Length + TagLength];
        token[0] = _activeId; token[1] = version;
        RandomNumberGenerator.Fill(token.AsSpan(2, NonceLength));
        byte[] associated = Associated(purpose, version, scopeDigest);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(token.AsSpan(2, NonceLength), plaintext, token.AsSpan(2 + NonceLength, plaintext.Length), token.AsSpan(2 + NonceLength + plaintext.Length, TagLength), associated);
            return Encode(token);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public BaseOpaqueTokenResult Unprotect(string purpose, byte version, string tokenText, int expectedPlaintextLength, ReadOnlySpan<byte> scopeDigest)
        => Unprotect(purpose, version, tokenText, expectedPlaintextLength, expectedPlaintextLength, scopeDigest);

    public BaseOpaqueTokenResult Unprotect(
        string purpose,
        byte version,
        string tokenText,
        int minimumPlaintextLength,
        int maximumPlaintextLength,
        ReadOnlySpan<byte> scopeDigest)
    {
        if (string.IsNullOrWhiteSpace(purpose) || scopeDigest.Length != 32 || string.IsNullOrWhiteSpace(tokenText) || minimumPlaintextLength < 0 || maximumPlaintextLength < minimumPlaintextLength || maximumPlaintextLength > 65_536)
            return new(BaseOpaqueTokenStatus.Invalid, null);
        byte[] token;
        try { token = Decode(tokenText); } catch (FormatException) { return new(BaseOpaqueTokenStatus.Invalid, null); }
        int plaintextLength = token.Length - 2 - NonceLength - TagLength;
        if (plaintextLength < minimumPlaintextLength || plaintextLength > maximumPlaintextLength) return new(BaseOpaqueTokenStatus.Invalid, null);
        if (!_keys.TryGetValue(token[0], out KeyState? state)
            || state.DecryptUntil is { } decryptUntil && _timeProvider.GetUtcNow() >= decryptUntil)
            return new(BaseOpaqueTokenStatus.KeyUnavailable, null);
        if (token[1] != version) return new(BaseOpaqueTokenStatus.VersionUnsupported, null);
        byte[] plaintext = new byte[plaintextLength];
        byte[] key = Derive(state.Key, purpose, version);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(token.AsSpan(2, NonceLength), token.AsSpan(2 + NonceLength, plaintextLength), token.AsSpan(2 + NonceLength + plaintextLength, TagLength), plaintext, Associated(purpose, version, scopeDigest));
            return new(BaseOpaqueTokenStatus.Valid, plaintext);
        }
        catch (CryptographicException) { CryptographicOperations.ZeroMemory(plaintext); return new(BaseOpaqueTokenStatus.Invalid, null); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public void Dispose() { foreach (KeyState state in _keys.Values) CryptographicOperations.ZeroMemory(state.Key); _keys.Clear(); }

    internal byte ActiveKeyId => _activeId;

    internal byte[] Authenticate(string purpose, byte keyId, ReadOnlySpan<byte> value)
    {
        if (!_keys.TryGetValue(keyId, out KeyState? state))
            throw new KeyNotFoundException("The requested authentication key is unavailable.");
        if (state.DecryptUntil is { } decryptUntil && _timeProvider.GetUtcNow() >= decryptUntil)
            throw new KeyNotFoundException("The requested authentication key is unavailable.");
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, state.Key, 32, info: Encoding.UTF8.GetBytes(purpose));
        try { return HMACSHA256.HashData(key, value); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal bool HasKey(byte keyId) => _keys.TryGetValue(keyId, out KeyState? state)
        && (state.DecryptUntil is null || _timeProvider.GetUtcNow() < state.DecryptUntil);

    private void Add(BaseOpaqueTokenKey key)
    {
        if (key.Key is not { Length: 32 } || !_keys.TryAdd(key.Id, new KeyState(
                [.. key.Key],
                key.IssueNotBefore,
                key.IssueUntil,
                key.DecryptUntil)))
            throw new ArgumentException("Token keys must have unique IDs and exactly 32 bytes.");
    }

    private sealed record KeyState(
        byte[] Key,
        DateTimeOffset IssueNotBefore,
        DateTimeOffset? IssueUntil,
        DateTimeOffset? DecryptUntil);
    private static byte[] Derive(byte[] root, string purpose, byte version)
    {
        byte[] info = Encoding.UTF8.GetBytes("hpd.base.token\0" + purpose + "\0" + version);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, info: info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(info);
        }
    }
    private static byte[] Associated(string purpose, byte version, ReadOnlySpan<byte> scope) => [.. Encoding.UTF8.GetBytes(purpose), 0, version, .. scope];
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) { string text = value.Replace('-', '+').Replace('_', '/'); int remainder = text.Length % 4; if (remainder != 0) text = text.PadRight(text.Length + 4 - remainder, '='); return Convert.FromBase64String(text); }
}
