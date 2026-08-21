using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Contains opaque authenticated text-index consistency authority.</summary>
public readonly struct BaseTextConsistencyToken : IEquatable<BaseTextConsistencyToken>
{
    private readonly ImmutableArray<byte> _bytes;
    private BaseTextConsistencyToken(ImmutableArray<byte> bytes) => _bytes = ImmutableArray.Create(bytes.ToArray());
    /// <summary>Parses bounded token syntax without granting authority.</summary>
    public static BaseTextConsistencyToken Parse(string value) => TryParse(value, out BaseTextConsistencyToken result) ? result : throw new FormatException(BaseTextErrorCodes.ConsistencyInvalid);
    /// <summary>Attempts to parse bounded token syntax without granting authority.</summary>
    public static bool TryParse(string? value, out BaseTextConsistencyToken result)
    {
        result = default;
        if (string.IsNullOrEmpty(value) || value.Length > 16 * 1024 || value.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) return false;
        result = new(ImmutableArray.Create(Encoding.ASCII.GetBytes(value)));
        return true;
    }
    /// <summary>Returns the protected transport representation.</summary>
    public string Encode() => _bytes.IsDefaultOrEmpty ? throw new InvalidOperationException(BaseTextErrorCodes.ConsistencyInvalid) : Encoding.ASCII.GetString(_bytes.AsSpan());
    internal static BaseTextConsistencyToken Create(string value) => Parse(value);
    /// <inheritdoc />
    public bool Equals(BaseTextConsistencyToken other) => !_bytes.IsDefault && !other._bytes.IsDefault && CryptographicOperations.FixedTimeEquals(_bytes.AsSpan(), other._bytes.AsSpan());
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseTextConsistencyToken other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _bytes.IsDefault ? 0 : BitConverter.ToInt32(SHA256.HashData(_bytes.AsSpan()));
    /// <inheritdoc />
    public override string ToString() => "BaseTextConsistencyToken[redacted]";
}
