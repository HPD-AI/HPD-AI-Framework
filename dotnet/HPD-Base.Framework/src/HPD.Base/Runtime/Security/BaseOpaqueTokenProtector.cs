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
    private readonly Dictionary<byte, byte[]> _keys;

    public BaseOpaqueTokenProtector(IOptions<HPDBaseTokenProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BaseOpaqueTokenKey active = options.Value.ActiveKey ?? throw new ArgumentException("An active token key is required.", nameof(options));
        _activeId = active.Id;
        _keys = new Dictionary<byte, byte[]>();
        Add(active);
        foreach (BaseOpaqueTokenKey key in options.Value.DecryptionKeys ?? []) Add(key);
    }

    public string Protect(string purpose, byte version, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> scopeDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (plaintext.Length > 65_536) throw new ArgumentOutOfRangeException(nameof(plaintext));
        if (scopeDigest.Length != 32) throw new ArgumentOutOfRangeException(nameof(scopeDigest));
        byte[] key = Derive(_keys[_activeId], purpose, version);
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
        if (!_keys.TryGetValue(token[0], out byte[]? root)) return new(BaseOpaqueTokenStatus.KeyUnavailable, null);
        if (token[1] != version) return new(BaseOpaqueTokenStatus.VersionUnsupported, null);
        byte[] plaintext = new byte[plaintextLength];
        byte[] key = Derive(root, purpose, version);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(token.AsSpan(2, NonceLength), token.AsSpan(2 + NonceLength, plaintextLength), token.AsSpan(2 + NonceLength + plaintextLength, TagLength), plaintext, Associated(purpose, version, scopeDigest));
            return new(BaseOpaqueTokenStatus.Valid, plaintext);
        }
        catch (CryptographicException) { CryptographicOperations.ZeroMemory(plaintext); return new(BaseOpaqueTokenStatus.Invalid, null); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public void Dispose() { foreach (byte[] key in _keys.Values) CryptographicOperations.ZeroMemory(key); _keys.Clear(); }

    private void Add(BaseOpaqueTokenKey key)
    {
        if (key.Key is not { Length: 32 } || !_keys.TryAdd(key.Id, [.. key.Key]))
            throw new ArgumentException("Token keys must have unique IDs and exactly 32 bytes.");
    }
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
