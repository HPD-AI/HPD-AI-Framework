using HPD.Base;
using System.Text.Json;

namespace HPD.Base.Sqlite;

internal static class SqliteRecordSerializer
{
    /// <summary>Executes the normalize object payload operation.</summary>
    public static RecordPayload NormalizeObjectPayload(RecordPayload payload)
    {
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            var fields = payload.Fields?.ToDictionary(field => field.Key, field => field.Value.Clone(), StringComparer.Ordinal)
                ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        if (payload.Json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("SQLite record payloads must be JSON objects or field maps.");
        }

        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = payload.Json.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
        };
    }

    /// <summary>Executes the serialize operation.</summary>
    public static string Serialize(RecordPayload payload)
    {
        var normalized = NormalizeObjectPayload(payload);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var field in normalized.Fields is null
                ? Enumerable.Empty<KeyValuePair<string, JsonElement>>()
                : normalized.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(field.Key);
                field.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Executes the deserialize operation.</summary>
    public static RecordPayload Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
        };
    }

    /// <summary>Executes the merge operation.</summary>
    public static RecordPayload Merge(RecordPayload current, RecordPayload patch)
    {
        var fields = NormalizeObjectPayload(current).Fields?.ToDictionary(field => field.Key, field => field.Value.Clone(), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in NormalizeObjectPayload(patch).Fields ?? [])
        {
            fields[field.Key] = field.Value.Clone();
        }

        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
    }

    /// <summary>Executes the select operation.</summary>
    public static RecordPayload Select(RecordPayload payload, string[]? select)
    {
        if (select is null || select.Length == 0)
        {
            return Clone(payload);
        }

        var normalized = NormalizeObjectPayload(payload);
        var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in select)
        {
            if (normalized.Fields?.TryGetValue(field, out var value) == true)
            {
                selected[field] = value.Clone();
            }
        }

        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = selected };
    }

    /// <summary>Executes the clone operation.</summary>
    public static RecordPayload Clone(RecordPayload payload) => NormalizeObjectPayload(payload);
}
