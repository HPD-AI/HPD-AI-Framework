using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Provides the reflection-free canonical provider-protocol encoding for canonical-JSON query bytes.</summary>
public sealed class BaseCanonicalJsonQueryValueJsonConverter : JsonConverter<ImmutableArray<byte>>
{
    private const int MaximumDecodedBytes = 1_048_576;

    /// <inheritdoc />
    public override ImmutableArray<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.HasValueSequence)
            throw new JsonException("base.relational.read.invalid");
        ReadOnlySpan<byte> encoded = reader.ValueSpan;
        if (encoded.Length == 0 || encoded.Length > ((MaximumDecodedBytes + 2) / 3) * 4 || encoded.Length % 4 != 0)
            throw new JsonException("base.relational.read.invalid");
        string text = Encoding.UTF8.GetString(encoded);
        if (text.Any(static value => !(value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=')))
            throw new JsonException("base.relational.read.invalid");
        byte[] decoded;
        try { decoded = Convert.FromBase64String(text); }
        catch (FormatException exception) { throw new JsonException("base.relational.read.invalid", exception); }
        if (decoded.Length is 0 or > MaximumDecodedBytes || !string.Equals(Convert.ToBase64String(decoded), text, StringComparison.Ordinal))
            throw new JsonException("base.relational.read.invalid");
        return ImmutableArray.Create(decoded);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ImmutableArray<byte> value, JsonSerializerOptions options)
    {
        if (value.IsDefaultOrEmpty || value.Length > MaximumDecodedBytes)
            throw new JsonException("base.relational.read.invalid");
        writer.WriteBase64StringValue(value.AsSpan());
    }
}
