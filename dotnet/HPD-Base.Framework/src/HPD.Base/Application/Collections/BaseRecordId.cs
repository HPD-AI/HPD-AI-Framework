using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>
/// Identifies one record in the collection represented by
/// <typeparamref name="TRecord"/>.
/// </summary>
/// <typeparam name="TRecord">The collection's persisted record type.</typeparam>
[JsonConverter(typeof(BaseRecordIdJsonConverterFactory))]
public readonly record struct BaseRecordId<TRecord>(RecordId Value)
{
    /// <summary>Creates a typed record identifier from its wire value.</summary>
    /// <param name="value">The canonical record identifier string.</param>
    /// <returns>The validated typed identifier.</returns>
    public static BaseRecordId<TRecord> Create(string value) => new(RecordId.Create(value));

    /// <summary>Parses a typed record identifier.</summary>
    /// <param name="value">The canonical record identifier string.</param>
    /// <returns>The validated typed identifier.</returns>
    public static BaseRecordId<TRecord> Parse(string value) => Create(value);

    /// <summary>Attempts to parse a typed record identifier.</summary>
    public static bool TryParse(string? value, out BaseRecordId<TRecord> result)
    {
        if (RecordId.TryParse(value, out var id))
        {
            result = new BaseRecordId<TRecord>(id);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Converts explicitly to the canonical Runtime identifier.</summary>
    public static explicit operator RecordId(BaseRecordId<TRecord> value) => value.Value;

    /// <summary>Converts explicitly from the canonical Runtime identifier.</summary>
    public static explicit operator BaseRecordId<TRecord>(RecordId value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.Value;
}

/// <summary>Creates closed scalar JSON converters for typed record identifiers.</summary>
public sealed class BaseRecordIdJsonConverterFactory : JsonConverterFactory
{
    private static readonly Dictionary<Type, JsonConverter> Converters = [];
    private static readonly Lock Sync = new();

    /// <summary>Registers one generator-discovered closed typed identifier.</summary>
    /// <typeparam name="TRecord">The generated collection record type.</typeparam>
    public static void Register<TRecord>()
    {
        lock (Sync)
        {
            Converters.TryAdd(
                typeof(BaseRecordId<TRecord>),
                new BaseRecordIdJsonConverter<TRecord>());
        }
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(BaseRecordId<>);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        lock (Sync)
        {
            return Converters.TryGetValue(typeToConvert, out var converter)
                ? converter
                : throw new NotSupportedException(
                    "Typed record id JSON metadata was not generated for this closed type.");
        }
    }

    private sealed class BaseRecordIdJsonConverter<TRecord> : JsonConverter<BaseRecordId<TRecord>>
    {
        /// <summary>Executes the read operation.</summary>
        public override BaseRecordId<TRecord> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String ||
                !BaseRecordId<TRecord>.TryParse(reader.GetString(), out var value))
            {
                throw new JsonException("Typed record id must be a valid JSON string.");
            }

            return value;
        }

        /// <summary>Executes the write operation.</summary>
        public override void Write(
            Utf8JsonWriter writer,
            BaseRecordId<TRecord> value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value.Value);
    }
}
