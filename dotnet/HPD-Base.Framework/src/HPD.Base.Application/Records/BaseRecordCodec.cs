using System.Buffers;
using System.Text.Json;
using HPD.Base.Application.Collections;
using HPD.Base.Records;

namespace HPD.Base.Application.Records;

internal static class BaseRecordCodec
{
    public static RecordPayload Encode<T>(
        BaseCollection<T> collection,
        T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = JsonSerializer.SerializeToElement(value, collection.JsonTypeInfo),
        };
    }

    public static BaseRecord<T> Decode<T>(
        BaseCollection<T> collection,
        RecordEnvelope envelope)
    {
        JsonElement payload = envelope.Payload.Kind switch
        {
            RecordPayloadKind.Json => envelope.Payload.Json,
            RecordPayloadKind.FieldMap => FieldMapElement(envelope.Payload.Fields),
            _ => throw new InvalidOperationException(
                "BASE returned an unsupported record payload kind."),
        };

        T value = JsonSerializer.Deserialize(payload, collection.JsonTypeInfo)
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
