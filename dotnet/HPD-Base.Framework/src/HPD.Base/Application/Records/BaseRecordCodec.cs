using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal static class BaseRecordCodec
{
    /// <summary>Executes the encode operation.</summary>
    public static RecordPayload Encode<T>(
        BaseCollection<T> collection,
        T value)
        => Encode(value, collection.JsonTypeInfo);

    /// <summary>Executes the encode operation.</summary>
    public static RecordPayload Encode<T>(
        T value,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = JsonSerializer.SerializeToElement(value, jsonTypeInfo),
        };
    }

    /// <summary>Encodes one source-generated object as a portable top-level field-map patch.</summary>
    public static RecordPayload EncodePatch<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        JsonElement encoded = JsonSerializer.SerializeToElement(value, jsonTypeInfo);
        if (encoded.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A typed patch must serialize as a JSON object.", nameof(value));
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in encoded.EnumerateObject())
            fields.Add(property.Name, property.Value.Clone());
        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
    }

    /// <summary>Executes the decode operation.</summary>
    public static BaseRecord<T> Decode<T>(
        BaseCollection<T> collection,
        RecordEnvelope envelope)
        => Decode(collection.JsonTypeInfo, envelope);

    internal static BaseRecord<T> Decode<T>(
        JsonTypeInfo<T> jsonTypeInfo,
        RecordEnvelope envelope)
    {
        JsonElement payload = envelope.Payload.Kind switch
        {
            RecordPayloadKind.Json => envelope.Payload.Json,
            RecordPayloadKind.FieldMap => FieldMapElement(envelope.Payload.Fields),
            _ => throw new InvalidOperationException(
                "BASE returned an unsupported record payload kind."),
        };

        T value = JsonSerializer.Deserialize(payload, jsonTypeInfo)
            ?? throw new InvalidOperationException(
                "BASE returned a null typed record payload.");

        return new BaseRecord<T>
        {
            Id = envelope.Id,
            Value = value,
            Revision = envelope.Metadata.Revision,
            CreatedAt = envelope.Metadata.CreatedAt,
            UpdatedAt = envelope.Metadata.UpdatedAt,
            Redacted = envelope.Policy?.Redacted == true,
        };
    }

    private static JsonElement FieldMapElement(
        IReadOnlyDictionary<string, JsonElement>? fields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (fields is not null)
            {
                foreach (var pair in fields.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(pair.Key);
                    pair.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
