using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Gateway;

internal sealed class StrictStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Enum values must be strings.");
        }

        var value = reader.GetString();
        if (value is null)
        {
            throw new JsonException("Enum value is required.");
        }

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(JsonNamingPolicy.CamelCase.ConvertName(candidate.ToString()), value, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new JsonException("Enum value is unsupported.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException("Enum value is unsupported.");
        }

        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
