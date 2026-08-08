using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseAtomicMutationProjectionFactory
{
    public static BaseAtomicMutationProjectionRequest Create(
        IReadOnlyList<BaseRecordMutationFact> mutations,
        BaseCollectionPurgeProjectionFact? purge = null)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        var projected = ImmutableArray.CreateBuilder<BaseAtomicMutationProjectionFact>(mutations.Count);
        foreach (BaseRecordMutationFact mutation in mutations)
            projected.Add(Project(mutation));
        return new BaseAtomicMutationProjectionRequest(projected.MoveToImmutable(), purge);
    }

    public static BaseCollectionPurgeProjectionFact Purge(
        string collectionId,
        long previousGeneration,
        long publishedGeneration) =>
        new(Copy(collectionId), previousGeneration, publishedGeneration);

    private static BaseAtomicMutationProjectionFact Project(BaseRecordMutationFact mutation)
    {
        Dictionary<string, FieldDefinition> fieldsByName = (mutation.Collection.Fields ?? [])
            .ToDictionary(static field => field.Name, StringComparer.Ordinal);
        ImmutableArray<string> changed = (mutation.ChangedFields ?? [])
            .Select(name => fieldsByName.TryGetValue(name, out FieldDefinition? field) ? field.Id : name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(Copy)
            .ToImmutableArray();

        return new BaseAtomicMutationProjectionFact(
            CopyNullable(mutation.ItemId),
            mutation.RequestedOperation,
            mutation.CommittedOperation,
            mutation.UpsertOutcome,
            Copy(mutation.Collection.Id),
            Copy(mutation.Event.EventId),
            mutation.JournalPosition,
            ProjectRecord(mutation.Before, fieldsByName),
            ProjectRecord(mutation.After, fieldsByName),
            changed);
    }

    private static BaseAtomicProjectionRecord? ProjectRecord(
        RecordEnvelope? record,
        IReadOnlyDictionary<string, FieldDefinition> fieldsByName)
    {
        if (record is null)
            return null;
        RevisionToken revision = record.Metadata.Revision
            ?? throw new InvalidOperationException("A transactional projection record requires a revision.");
        IEnumerable<KeyValuePair<string, JsonElement>> fields = record.Payload.Kind switch
        {
            RecordPayloadKind.FieldMap => record.Payload.Fields ?? [],
            RecordPayloadKind.Json when record.Payload.Json.ValueKind == JsonValueKind.Object =>
                record.Payload.Json.EnumerateObject().Select(static property =>
                    new KeyValuePair<string, JsonElement>(property.Name, property.Value)),
            _ => throw new InvalidOperationException("A transactional projection record requires an object payload."),
        };

        var projected = ImmutableArray.CreateBuilder<BaseAtomicProjectionField>();
        foreach ((string name, JsonElement value) in fields)
        {
            if (!fieldsByName.TryGetValue(name, out FieldDefinition? field))
                continue;
            byte[] utf8 = CanonicalBytes(value);
            projected.Add(new BaseAtomicProjectionField(
                Copy(field.Id),
                new BaseAtomicProjectionValue(Kind(value), ImmutableArray.CreateRange(utf8))));
        }

        projected.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.StableFieldId, right.StableFieldId));
        return new BaseAtomicProjectionRecord(record.Id, revision, projected.ToImmutable());
    }

    private static BaseAtomicProjectionValueKind Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => BaseAtomicProjectionValueKind.Null,
        JsonValueKind.True or JsonValueKind.False => BaseAtomicProjectionValueKind.Boolean,
        JsonValueKind.Number when value.TryGetInt64(out _) => BaseAtomicProjectionValueKind.Integer,
        JsonValueKind.Number => BaseAtomicProjectionValueKind.Number,
        JsonValueKind.String => BaseAtomicProjectionValueKind.String,
        JsonValueKind.Array => BaseAtomicProjectionValueKind.Array,
        JsonValueKind.Object => BaseAtomicProjectionValueKind.Object,
        _ => throw new InvalidOperationException("Unsupported transactional projection JSON value."),
    };

    private static byte[] CanonicalBytes(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            value.WriteTo(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static string Copy(string value) => new(value.AsSpan());
    private static string? CopyNullable(string? value) => value is null ? null : Copy(value);
}
