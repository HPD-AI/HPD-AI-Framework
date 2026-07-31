using System.Text.Json;

namespace HPD.Base;

internal static class RecordCloneHelpers
{
    public static RecordEnvelope CloneEnvelope(StoredRecord record) => new()
    {
        CollectionId = record.CollectionId,
        Id = record.Id,
        Payload = ClonePayload(record.Payload),
        Metadata = CloneMetadata(record.Metadata)
    };

    public static RecordPayload ClonePayload(RecordPayload payload)
    {
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            return new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = CloneFields(payload.Fields)
            };
        }

        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = payload.Json.Clone()
        };
    }

    public static RecordMetadata CloneMetadata(RecordMetadata metadata) => new()
    {
        CreatedAt = metadata.CreatedAt,
        UpdatedAt = metadata.UpdatedAt,
        Revision = metadata.Revision,
        ETag = metadata.ETag,
        StoreId = metadata.StoreId,
        Tags = metadata.Tags is null
            ? null
            : new Dictionary<string, string>(metadata.Tags, StringComparer.Ordinal)
    };

    public static Dictionary<string, JsonElement> CloneFields(Dictionary<string, JsonElement>? fields)
    {
        var clone = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in fields ?? [])
        {
            clone[field.Key] = field.Value.Clone();
        }

        return clone;
    }
}
