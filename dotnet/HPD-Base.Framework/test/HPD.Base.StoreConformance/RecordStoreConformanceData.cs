using System.Buffers;
using System.Text.Json;

namespace HPD.Base.StoreConformance;

public static class RecordStoreConformanceData
{
    public static RecordPayload Payload(params (string Field, string Value)[] fields)
    {
        using var document = JsonDocument.Parse("{" + string.Join(",", fields.Select(field => $"\"{field.Field}\":\"{field.Value}\"")) + "}");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    public static RecordPayload JsonPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    public static RecordPayload Patch(params (string Field, JsonElement Value)[] fields) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = fields.ToDictionary(field => field.Field, field => field.Value.Clone(), StringComparer.Ordinal)
    };

    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement StringElement(string value) => Element(JsonSerializer.Serialize(value));

    public static RecordPayload InvalidScalarPayload() => JsonPayload("\"not-an-object\"");

    public static Dictionary<string, JsonElement> Fields(params (string Field, string Value)[] fields) =>
        fields.ToDictionary(field => field.Field, field => StringElement(field.Value), StringComparer.Ordinal);

    public static RecordPayload FieldMap(params (string Field, string Value)[] fields) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = Fields(fields)
    };

    public static string RawJson(RecordPayload payload)
    {
        if (payload.Kind == RecordPayloadKind.Json)
        {
            return payload.Json.GetRawText();
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var field in payload.Fields ?? [])
            {
                writer.WritePropertyName(field.Key);
                field.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.GetRawText();
    }
}
