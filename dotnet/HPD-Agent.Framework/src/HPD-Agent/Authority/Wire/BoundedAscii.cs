using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Contains one to 256 printable or control-free ASCII characters for canonical authority fields.</summary>
/// <remarks>The default value is invalid and canonical authority boundaries reject it.</remarks>
public readonly struct BoundedAscii : IEquatable<BoundedAscii>, IComparable<BoundedAscii>
{
    /// <summary>The maximum encoded byte and character count.</summary>
    public const int MaximumLength = 256;

    private readonly string? _value;

    /// <summary>Initializes a validated bounded ASCII value.</summary>
    /// <param name="value">A nonempty ASCII string of at most 256 characters.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, contains a non-ASCII character, or contains an ASCII control character.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 256 characters.</exception>
    public BoundedAscii(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            throw new ArgumentException("A bounded ASCII value cannot be empty.", nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentOutOfRangeException(nameof(value), "A bounded ASCII value cannot exceed 256 characters.");
        foreach (var character in value)
        {
            if (character is < (char)0x20 or > (char)0x7e)
                throw new ArgumentException("A bounded ASCII value must contain only printable ASCII characters.", nameof(value));
        }
        _value = value;
    }

    /// <summary>Gets whether the value satisfies the canonical nonempty ASCII bounds.</summary>
    public bool IsValid => _value is not null;

    /// <summary>Returns the canonical ASCII text, or an empty string for the invalid default value.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Compares canonical bytes using ordinal order.</summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>A negative, zero, or positive ordinal comparison result.</returns>
    public int CompareTo(BoundedAscii other) => StringComparer.Ordinal.Compare(_value, other._value);

    /// <inheritdoc />
    public bool Equals(BoundedAscii other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BoundedAscii other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>Returns whether two bounded ASCII values contain the same canonical bytes.</summary>
    public static bool operator ==(BoundedAscii left, BoundedAscii right) => left.Equals(right);

    /// <summary>Returns whether two bounded ASCII values contain different canonical bytes.</summary>
    public static bool operator !=(BoundedAscii left, BoundedAscii right) => !left.Equals(right);

    internal ReadOnlySpan<char> Characters => _value.AsSpan();
}

internal static class BoundedAsciiCodec
{
    internal static void Write(CborWriter writer, BoundedAscii value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The bounded ASCII value is invalid.", nameof(value));
        writer.WriteTextString(value.Characters);
    }

    internal static BoundedAscii Read(CborReader reader)
    {
        Span<char> characters = stackalloc char[BoundedAscii.MaximumLength];
        if (!reader.TryReadTextString(characters, out var written) || written == 0)
            throw new CborContentException("A bounded ASCII value must contain one to 256 ASCII bytes.");
        for (var index = 0; index < written; index++)
        {
            if (characters[index] is < (char)0x20 or > (char)0x7e)
                throw new CborContentException("A bounded ASCII value contains a non-printable or non-ASCII character.");
        }
        return new BoundedAscii(new string(characters[..written]));
    }

    internal static byte[] Encode(BoundedAscii value)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Write(writer, value);
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out BoundedAscii value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            value = Read(reader);
            if (reader.BytesRemaining != 0)
            {
                value = default;
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = default;
            return false;
        }
    }
}
