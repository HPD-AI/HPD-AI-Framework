using System.Globalization;
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
            throw new JsonException("RecordId must be a canonical JSON string.");
        }

        if (!RecordId.TryParse(reader.GetString(), out RecordId value))
            throw new JsonException("RecordId must be a canonical JSON string.");
        return value;
    }

    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, RecordId value, JsonSerializerOptions options)
    {
        if (!value.IsValid)
            throw new JsonException("RecordId must be a canonical JSON string.");
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

        try { return new RevisionToken(reader.GetString() ?? throw new JsonException()); }
        catch (ArgumentException) { throw new JsonException(BaseSchemaErrorCodes.ContractInvalid); }
    }

    /// <summary>Executes the write operation.</summary>
    public override void Write(Utf8JsonWriter writer, RevisionToken value, JsonSerializerOptions options)
    {
        if (!value.IsValid) throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Provides the strict canonical lowercase <c>D</c>-format JSON codec for GUID values.</summary>
public sealed class BaseCanonicalGuidJsonConverter : JsonConverter<Guid>
{
    /// <inheritdoc />
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { Length: 36 } text
            || !Guid.TryParseExact(text, "D", out Guid value)
            || !string.Equals(text, value.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new JsonException("A GUID must be a canonical lowercase D-format JSON string.");
        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("D", CultureInfo.InvariantCulture));
}

/// <summary>Provides the strict canonical lowercase <c>D</c>-format JSON codec for nullable GUID values.</summary>
public sealed class BaseCanonicalNullableGuidJsonConverter : JsonConverter<Guid?>
{
    private static readonly BaseCanonicalGuidJsonConverter Inner = new();

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(Guid), options);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else Inner.Write(writer, value.Value, options);
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
