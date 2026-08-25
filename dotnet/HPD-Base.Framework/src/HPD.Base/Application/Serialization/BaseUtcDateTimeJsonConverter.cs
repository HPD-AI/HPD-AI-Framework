using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Serializes one UTC instant using the sole canonical BASE wire spelling.</summary>
public sealed class BaseUtcDateTimeJsonConverter : JsonConverterFactory
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(DateTimeOffset) || typeToConvert == typeof(DateTimeOffset?);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => typeToConvert == typeof(DateTimeOffset)
        ? new RequiredConverter() : typeToConvert == typeof(DateTimeOffset?) ? new NullableConverter() : throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);

    private static DateTimeOffset ReadValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String || !DateTimeOffset.TryParseExact(reader.GetString(), Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value) || value.Offset != TimeSpan.Zero)
            throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        return value;
    }

    private static void WriteValue(Utf8JsonWriter writer, DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }

    private sealed class RequiredConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadValue(ref reader);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => WriteValue(writer, value);
    }
    private sealed class NullableConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Null ? null : ReadValue(ref reader);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) { if (value is null) writer.WriteNullValue(); else WriteValue(writer, value.Value); }
    }
}
