using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Authority;

internal readonly struct StableId128 : IEquatable<StableId128>
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private readonly ulong _high;
    private readonly ulong _low;

    private StableId128(ulong high, ulong low)
    {
        if ((high | low) == 0)
            throw new ArgumentOutOfRangeException(nameof(high), "Authority IDs cannot be all zero.");
        _high = high;
        _low = low;
    }

    internal static StableId128 CreateRandom()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.IndexOfAnyExcept((byte)0) < 0);
        return FromBytes(bytes);
    }

    internal static StableId128 FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException("A stable ID is exactly 16 bytes.", nameof(bytes));
        return new(BinaryPrimitives.ReadUInt64BigEndian(bytes), BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));
    }

    internal static bool TryParse(string? text, string family, out StableId128 value)
    {
        value = default;
        if (text is null || text.Length != family.Length + 27 ||
            !text.AsSpan(0, family.Length).SequenceEqual(family) || text[family.Length] != ':')
            return false;

        ReadOnlySpan<char> encoded = text.AsSpan(family.Length + 1);
        if (encoded[0] is < '0' or > '7')
            return false;

        Span<byte> bytes = stackalloc byte[16];
        for (var digitIndex = 0; digitIndex < 26; digitIndex++)
        {
            var digit = Alphabet.IndexOf(encoded[digitIndex]);
            if (digit < 0)
                return false;
            for (var bit = 0; bit < 5; bit++)
            {
                var outputBit = digitIndex * 5 + bit - 2;
                if (outputBit >= 0 && (digit & (1 << (4 - bit))) != 0)
                    bytes[outputBit / 8] |= (byte)(1 << (7 - outputBit % 8));
            }
        }

        if (bytes.IndexOfAnyExcept((byte)0) < 0)
            return false;
        value = FromBytes(bytes);
        return true;
    }

    internal string Format(string family)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], _low);
        Span<char> result = stackalloc char[family.Length + 27];
        family.AsSpan().CopyTo(result);
        result[family.Length] = ':';
        for (var digitIndex = 0; digitIndex < 26; digitIndex++)
        {
            var digit = 0;
            for (var bit = 0; bit < 5; bit++)
            {
                var inputBit = digitIndex * 5 + bit - 2;
                if (inputBit >= 0 && (bytes[inputBit / 8] & (1 << (7 - inputBit % 8))) != 0)
                    digit |= 1 << (4 - bit);
            }
            result[family.Length + 1 + digitIndex] = Alphabet[digit];
        }
        return new string(result);
    }

    public bool Equals(StableId128 other) => _high == other._high && _low == other._low;
    public override bool Equals(object? obj) => obj is StableId128 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_high, _low);
}

/// <summary>Identifies a tenant in authority-bearing records.</summary>
/// <remarks>The canonical text form is <c>ten:</c> followed by 26 uppercase Crockford Base32 digits. The default value is invalid at authority boundaries.</remarks>
public readonly record struct TenantId
{
    private readonly StableId128 _value;
    private TenantId(StableId128 value) => _value = value;
    /// <summary>Allocates a cryptographically random tenant identifier.</summary>
    internal static TenantId Create() => new(StableId128.CreateRandom());
    /// <summary>Parses a canonical tenant identifier without accepting aliases or noncanonical text.</summary>
    public static bool TryParse(string? text, out TenantId value) { var ok = StableId128.TryParse(text, "ten", out var parsed); value = ok ? new(parsed) : default; return ok; }
    /// <summary>Returns the canonical text form, or an empty string for the invalid default value.</summary>
    public override string ToString() => _value.Equals(default) ? string.Empty : _value.Format("ten");
}

/// <summary>Identifies a durable Agent session used only as an explicit correlation.</summary>
/// <remarks>The canonical text form begins with <c>ses:</c>. It is not the live-session authority key.</remarks>
public readonly record struct SessionId
{
    private readonly StableId128 _value;
    private SessionId(StableId128 value) => _value = value;
    /// <summary>Allocates a cryptographically random session correlation identifier.</summary>
    internal static SessionId Create() => new(StableId128.CreateRandom());
    /// <summary>Parses a canonical session identifier.</summary>
    public static bool TryParse(string? text, out SessionId value) { var ok = StableId128.TryParse(text, "ses", out var parsed); value = ok ? new(parsed) : default; return ok; }
    /// <summary>Returns the canonical text form, or an empty string for the invalid default value.</summary>
    public override string ToString() => _value.Equals(default) ? string.Empty : _value.Format("ses");
}

/// <summary>Identifies an Agent thread correlation within durable history.</summary>
public readonly record struct ThreadId
{
    private readonly StableId128 _value;
    private ThreadId(StableId128 value) => _value = value;
    /// <summary>Allocates a cryptographically random thread identifier.</summary>
    internal static ThreadId Create() => new(StableId128.CreateRandom());
    /// <summary>Parses a canonical <c>thr:</c> identifier.</summary>
    public static bool TryParse(string? text, out ThreadId value) { var ok = StableId128.TryParse(text, "thr", out var parsed); value = ok ? new(parsed) : default; return ok; }
    /// <summary>Returns the canonical text form, or an empty string for the invalid default value.</summary>
    public override string ToString() => _value.Equals(default) ? string.Empty : _value.Format("thr");
}

/// <summary>Identifies one logical live Audio session across reconnects and participant replacement.</summary>
public readonly record struct LiveSessionId
{
    private readonly StableId128 _value;
    private LiveSessionId(StableId128 value) => _value = value;
    /// <summary>Allocates a cryptographically random live-session identifier.</summary>
    internal static LiveSessionId Create() => new(StableId128.CreateRandom());
    /// <summary>Parses a canonical <c>liv:</c> identifier.</summary>
    public static bool TryParse(string? text, out LiveSessionId value) { var ok = StableId128.TryParse(text, "liv", out var parsed); value = ok ? new(parsed) : default; return ok; }
    /// <summary>Returns the canonical text form, or an empty string for the invalid default value.</summary>
    public override string ToString() => _value.Equals(default) ? string.Empty : _value.Format("liv");
}

/// <summary>Identifies the S1-owned runtime generation that fences stale callbacks and commands.</summary>
public readonly record struct RuntimeGenerationId
{
    private readonly StableId128 _value;
    private RuntimeGenerationId(StableId128 value) => _value = value;
    /// <summary>Allocates a cryptographically random runtime-generation identifier.</summary>
    internal static RuntimeGenerationId Create() => new(StableId128.CreateRandom());
    /// <summary>Parses a canonical <c>run:</c> identifier.</summary>
    public static bool TryParse(string? text, out RuntimeGenerationId value) { var ok = StableId128.TryParse(text, "run", out var parsed); value = ok ? new(parsed) : default; return ok; }
    /// <summary>Returns the canonical text form, or an empty string for the invalid default value.</summary>
    public override string ToString() => _value.Equals(default) ? string.Empty : _value.Format("run");
}

/// <summary>Represents an immutable 256-bit authority hash.</summary>
/// <remarks>Diagnostic text is exactly 64 lowercase hexadecimal characters. The default value is invalid at authority boundaries.</remarks>
public readonly struct Hash256 : IEquatable<Hash256>
{
    private readonly byte[]? _bytes;
    private Hash256(byte[] bytes) => _bytes = bytes;
    /// <summary>Computes SHA-256 over the supplied bytes.</summary>
    internal static Hash256 Compute(ReadOnlySpan<byte> bytes) => new(SHA256.HashData(bytes));
    /// <summary>Parses the canonical lowercase hexadecimal form.</summary>
    public static bool TryParse(string? text, out Hash256 value)
    {
        value = default;
        if (text is null || text.Length != 64 || text.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return false;
        value = new(Convert.FromHexString(text));
        return true;
    }
    /// <summary>Returns canonical lowercase hexadecimal, or an empty string for the invalid default value.</summary>
    public override string ToString() => _bytes is null ? string.Empty : Convert.ToHexString(_bytes).ToLowerInvariant();
    /// <inheritdoc />
    public bool Equals(Hash256 other) =>
        _bytes is null ? other._bytes is null : other._bytes is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Hash256 other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _bytes is null ? 0 : HashCode.Combine(BinaryPrimitives.ReadUInt64BigEndian(_bytes), BinaryPrimitives.ReadUInt64BigEndian(_bytes.AsSpan(24)));
    /// <summary>Returns whether two hashes contain the same 256 bits.</summary>
    public static bool operator ==(Hash256 left, Hash256 right) => left.Equals(right);
    /// <summary>Returns whether two hashes contain different 256-bit values.</summary>
    public static bool operator !=(Hash256 left, Hash256 right) => !left.Equals(right);
}

/// <summary>Represents signed nanoseconds since the Unix epoch for evidence and display time.</summary>
/// <remarks>Journal position, not this timestamp, defines authority order.</remarks>
public readonly record struct UtcInstant
{
    /// <summary>Initializes an instant from an exact signed nanosecond count.</summary>
    public UtcInstant(long nanosecondsSinceUnixEpoch) => NanosecondsSinceUnixEpoch = nanosecondsSinceUnixEpoch;
    /// <summary>Gets signed nanoseconds since the Unix epoch.</summary>
    public long NanosecondsSinceUnixEpoch { get; }
}

/// <summary>Represents a checked signed duration measured in nanoseconds.</summary>
public readonly record struct DurationNs
{
    /// <summary>Initializes a duration from an exact signed nanosecond count.</summary>
    public DurationNs(long nanoseconds) => Nanoseconds = nanoseconds;
    /// <summary>Gets the signed nanosecond count.</summary>
    public long Nanoseconds { get; }
}
