using System.Text.Json;

namespace HPD.Base;

internal static class RecordCloneHelpers
{
    public static RecordEnvelope CloneEnvelope(RecordEnvelope record) => record with
    {
        Payload = ClonePayload(record.Payload),
        Metadata = CloneMetadata(record.Metadata),
    };

    /// <summary>Executes the clone envelope operation.</summary>
    public static RecordEnvelope CloneEnvelope(StoredRecord record) => new()
    {
        CollectionId = record.CollectionId,
        Id = record.Id,
        Payload = ClonePayload(record.Payload),
        Metadata = CloneMetadata(record.Metadata)
    };

    /// <summary>Executes the clone payload operation.</summary>
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

    /// <summary>Executes the clone metadata operation.</summary>
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

    /// <summary>Executes the clone fields operation.</summary>
    public static Dictionary<string, JsonElement> CloneFields(Dictionary<string, JsonElement>? fields)
    {
        var clone = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in fields ?? [])
        {
            clone[field.Key] = field.Value.Clone();
        }

        return clone;
    }

    public static BaseRecordMutationFact CloneMutationFact(BaseRecordMutationFact fact) => fact with
    {
        Before = fact.Before is null ? null : CloneEnvelope(fact.Before),
        After = fact.After is null ? null : CloneEnvelope(fact.After),
        Delete = fact.Delete is null ? null : fact.Delete with
        {
            Previous = fact.Delete.Previous is null ? null : CloneEnvelope(fact.Delete.Previous),
        },
        ChangedFields = fact.ChangedFields is null ? null : [.. fact.ChangedFields],
    };
}
