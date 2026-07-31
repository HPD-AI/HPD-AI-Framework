using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

public sealed class RecordIdJsonConverter : JsonConverter<RecordId>
{
    public override RecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("RecordId must be encoded as a JSON string.");
        }

        return new RecordId(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, RecordId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

public sealed class RevisionTokenJsonConverter : JsonConverter<RevisionToken>
{
    public override RevisionToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("RevisionToken must be encoded as a JSON string.");
        }

        return new RevisionToken(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, RevisionToken value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

public sealed class LowerCamelJsonStringEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public LowerCamelJsonStringEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}
