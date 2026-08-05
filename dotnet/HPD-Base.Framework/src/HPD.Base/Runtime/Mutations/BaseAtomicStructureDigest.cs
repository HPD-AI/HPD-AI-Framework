using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseAtomicStructureDigest
{
    public static byte[] Compute(BaseMutationCommand[] commands)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "base.atomic.structure.v1");
            writer.WriteString("store", commands[0].Store.Registration.StoreId.Normalize());
            writer.WriteStartArray("items");
            foreach (BaseMutationCommand command in commands.OrderBy(static command => command.Index))
            {
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", command.Index);
                writer.WriteNumber("kind", (int)command.Kind);
                writer.WriteString("collection", command.CollectionId.Normalize());
                WriteNullable(writer, "record", TargetId(command));
                WriteNullable(writer, "revision", ExpectedRevision(command));
                if (command.Upsert is { } upsert)
                {
                    writer.WriteNumber("upsertMode", (int)upsert.UpdateMode);
                    writer.WriteNumber("existence", (int)upsert.Condition);
                }
                WritePayload(writer, "create", command.CreatePayload?.Payload);
                WritePayload(writer, "update", command.UpdatePayload?.Payload);
                writer.WriteStartArray("changedFields");
                foreach (string field in (command.CreatePayload?.ChangedFields ?? command.UpdatePayload?.ChangedFields ?? []).Order(StringComparer.Ordinal))
                    writer.WriteStringValue(field.Normalize());
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    private static string? TargetId(BaseMutationCommand command) =>
        command.RecordId?.Value ?? command.Create?.RequestedId?.Value ?? command.Upsert?.Id.Value;

    private static string? ExpectedRevision(BaseMutationCommand command) =>
        command.Patch?.ExpectedRevision?.Value ?? command.Replace?.ExpectedRevision?.Value
        ?? command.Delete?.ExpectedRevision?.Value ?? command.Upsert?.ExpectedRevision?.Value;

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteString(name, value.Normalize());
    }

    private static void WritePayload(Utf8JsonWriter writer, string name, RecordPayload? payload)
    {
        writer.WritePropertyName(name);
        if (payload is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)payload.Kind);
        writer.WritePropertyName("value");
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            writer.WriteStartObject();
            foreach ((string key, JsonElement value) in (payload.Fields ?? []).OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key.Normalize());
                WriteJson(writer, value);
            }
            writer.WriteEndObject();
        }
        else WriteJson(writer, payload.Json);
        writer.WriteEndObject();
    }

    private static void WriteJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name.Normalize());
                    WriteJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray()) WriteJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
