using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Represents a record ID JSON converter.</summary>
public sealed class RecordIdJsonConverter : JsonConverter<RecordId>
{
    /// <summary>Executes the read operation.</summary>
    public override RecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("RecordId must be encoded as a JSON string.");
        }

        return new RecordId(reader.GetString() ?? string.Empty);
    }

    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, RecordId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Represents a revision token JSON converter.</summary>
public sealed class RevisionTokenJsonConverter : JsonConverter<RevisionToken>
{
    /// <summary>Executes the read operation.</summary>
    public override RevisionToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("RevisionToken must be encoded as a JSON string.");
        }

        return new RevisionToken(reader.GetString() ?? string.Empty);
    }

    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, RevisionToken value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Represents a lower camel JSON string enum converter.</summary>
public sealed class LowerCamelJsonStringEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>Initializes a new instance.</summary>
    public LowerCamelJsonStringEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}
