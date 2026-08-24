using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Owns an immutable, defensively copied binary field value.</summary>
[JsonConverter(typeof(BaseBinaryJsonConverter))]
public sealed class BaseBinary : IEquatable<BaseBinary>
{
    private readonly byte[] _bytes;
    private BaseBinary(byte[] bytes) => _bytes = bytes;

    /// <summary>Creates an owned binary value by copying <paramref name="value"/>.</summary>
    public static BaseBinary From(ReadOnlySpan<byte> value) => new(value.ToArray());
    /// <summary>Decodes one canonical RFC 4648 standard Base64 value.</summary>
    public static BaseBinary FromBase64(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return BaseBinaryJsonConverter.Decode(value);
    }
    /// <summary>Gets the decoded byte count.</summary>
    public int Length => _bytes.Length;
    /// <summary>Returns a fresh copy of the bytes.</summary>
    public byte[] ToArray() => _bytes.ToArray();
    /// <inheritdoc />
    public bool Equals(BaseBinary? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseBinary other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte value in _bytes) hash.Add(value);
        return hash.ToHashCode();
    }
    /// <inheritdoc />
    public override string ToString() => nameof(BaseBinary);
    internal ReadOnlySpan<byte> AsSpan() => _bytes;
}

/// <summary>Provides the canonical bounded Base64 wire contract for <see cref="BaseBinary"/>.</summary>
public sealed class BaseBinaryJsonConverter : JsonConverter<BaseBinary>
{
    private const int MaximumDecodedBytes = 1_048_576;
    /// <inheritdoc />
    public override BaseBinary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.HasValueSequence)
            throw new JsonException(BaseBinaryErrorCodes.EncodingInvalid);
        ReadOnlySpan<byte> encoded = reader.ValueSpan;
        if (encoded.Length > ((MaximumDecodedBytes + 2) / 3) * 4 || encoded.Length % 4 != 0)
            throw new JsonException(encoded.Length > MaximumDecodedBytes ? BaseBinaryErrorCodes.ValueTooLarge : BaseBinaryErrorCodes.EncodingInvalid);
        string text = Encoding.UTF8.GetString(encoded);
        try { return Decode(text); }
        catch (ArgumentOutOfRangeException exception) { throw new JsonException(BaseBinaryErrorCodes.ValueTooLarge, exception); }
        catch (FormatException exception) { throw new JsonException(BaseBinaryErrorCodes.EncodingInvalid, exception); }
    }
    internal static BaseBinary Decode(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > ((MaximumDecodedBytes + 2) / 3) * 4 || text.Length % 4 != 0)
            throw new FormatException(BaseBinaryErrorCodes.EncodingInvalid);
        if (text.Any(static character => !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=')))
            throw new FormatException(BaseBinaryErrorCodes.EncodingInvalid);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(text); }
        catch (FormatException) { throw new FormatException(BaseBinaryErrorCodes.EncodingInvalid); }
        if (bytes.Length > MaximumDecodedBytes)
            throw new ArgumentOutOfRangeException(nameof(text), BaseBinaryErrorCodes.ValueTooLarge);
        if (!string.Equals(Convert.ToBase64String(bytes), text, StringComparison.Ordinal))
            throw new FormatException(BaseBinaryErrorCodes.EncodingInvalid);
        return BaseBinary.From(bytes);
    }
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BaseBinary value, JsonSerializerOptions options) =>
        writer.WriteBase64StringValue(value.AsSpan());
}
