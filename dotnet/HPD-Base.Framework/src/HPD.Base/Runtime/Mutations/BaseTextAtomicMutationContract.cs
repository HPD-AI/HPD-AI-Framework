using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseTextAtomicMutationContract
{
    internal static BaseFinalizedTextMutationExtension? Finalize(ImmutableArray<BaseAtomicMutationPlanItem> items)
    {
        var facts = ImmutableArray.CreateBuilder<BaseTextProjectionFact>();
        foreach (BaseAtomicMutationPlanItem item in items.OrderBy(static value => value.Ordinal))
            foreach (BaseTextIndexDefinition index in (item.Collection.TextIndexes ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
                facts.Add(Fact(item.Ordinal, item.Collection, index, item.RecordId, item.Current, item.ProposedPayload, item.Operation, applied: false));
        if (facts.Count == 0) return null;
        ImmutableArray<BaseTextProjectionFact> frozen = facts.ToImmutable();
        return new() { Facts = frozen, ProjectionDigest = DigestFacts(frozen) };
    }

    internal static ImmutableArray<BasePreparedTextIndexEvidence> Indexes(BaseFinalizedTextMutationExtension? text, Func<string, string, long> generation)
    {
        if (text is null) return [];
        return text.Facts.Select(static fact => (fact.CollectionId, fact.TextIndexId, fact.TextIndexVersion, fact.TextIndexChecksum))
            .DistinctBy(static value => (value.CollectionId, value.TextIndexId, value.TextIndexVersion))
            .OrderBy(static value => value.CollectionId, StringComparer.Ordinal).ThenBy(static value => value.TextIndexId, StringComparer.Ordinal).ThenBy(static value => value.TextIndexVersion)
            .Select(value => new BasePreparedTextIndexEvidence { CollectionId = Copy(value.CollectionId), TextIndexId = Copy(value.TextIndexId), TextIndexVersion = value.TextIndexVersion, CapturedGeneration = generation(value.CollectionId, value.TextIndexId), TextIndexChecksum = Copy(value.TextIndexChecksum) })
            .ToImmutableArray();
    }

    internal static BasePreparedTextMutationEvidence? Prepare(BaseFinalizedTextMutationExtension? text, ImmutableArray<BasePreparedTextIndexEvidence> indexes) => text is null ? null : new()
    {
        ProjectionDigest = Copy(text.ProjectionDigest), Facts = text.Facts.Length, Indexes = indexes,
        EvidenceBytes = checked(4L + text.ProjectionDigest.Length + indexes.Sum(static value => 8L + Encoding.UTF8.GetByteCount(value.CollectionId) + Encoding.UTF8.GetByteCount(value.TextIndexId) + value.TextIndexChecksum.Length) + text.Facts.Sum(static fact => 4L + fact.FactChecksum.Length)),
    };

    internal static BaseAppliedTextMutationEvidence? Apply(BaseFinalizedTextMutationExtension? planned, IReadOnlyList<BaseRecordMutationFact> mutations, ImmutableArray<BasePreparedTextIndexEvidence> indexes)
    {
        if (planned is null) return null;
        var facts = ImmutableArray.CreateBuilder<BaseTextProjectionFact>(planned.Facts.Length);
        foreach (BaseTextProjectionFact expected in planned.Facts)
        {
            BaseRecordMutationFact mutation = mutations[expected.MutationOrdinal];
            BaseTextIndexDefinition index = (mutation.Collection.TextIndexes ?? []).Single(value => value.Id == expected.TextIndexId && value.Version == expected.TextIndexVersion);
            facts.Add(Fact(expected.MutationOrdinal, mutation.Collection, index, expected.RecordId, mutation.Before,
                mutation.After?.Payload, mutation.After?.Metadata.Revision, expected.After?.TenantId ?? expected.Before?.TenantId, expected.After?.ProjectId ?? expected.Before?.ProjectId));
        }
        ImmutableArray<BaseTextProjectionFact> applied = facts.MoveToImmutable();
        ImmutableArray<byte> digest = DigestFacts(applied);
        return new() { Facts = applied, Indexes = indexes.Select(static value => value with { CollectionId = Copy(value.CollectionId), TextIndexId = Copy(value.TextIndexId), TextIndexChecksum = Copy(value.TextIndexChecksum) }).ToImmutableArray(), EvidenceDigest = digest, EvidenceBytes = checked(4L + digest.Length + indexes.Sum(static value => 8L + Encoding.UTF8.GetByteCount(value.CollectionId) + Encoding.UTF8.GetByteCount(value.TextIndexId) + value.TextIndexChecksum.Length) + applied.Sum(static fact => 4L + fact.FactChecksum.Length)) };
    }

    internal static bool PreparedMatches(BaseFinalizedTextMutationExtension? planned, BasePreparedTextMutationEvidence? prepared)
    {
        if (planned is null) return prepared is null;
        if (prepared is null || prepared.Facts != planned.Facts.Length || !Equal(prepared.ProjectionDigest, planned.ProjectionDigest)) return false;
        ImmutableArray<(string CollectionId, string TextIndexId, int TextIndexVersion, ImmutableArray<byte> TextIndexChecksum)> expected = planned.Facts
            .Select(static fact => (fact.CollectionId, fact.TextIndexId, fact.TextIndexVersion, fact.TextIndexChecksum))
            .DistinctBy(static value => (value.CollectionId, value.TextIndexId, value.TextIndexVersion))
            .OrderBy(static value => value.CollectionId, StringComparer.Ordinal)
            .ThenBy(static value => value.TextIndexId, StringComparer.Ordinal)
            .ThenBy(static value => value.TextIndexVersion)
            .ToImmutableArray();
        if (prepared.Indexes.Length != expected.Length) return false;
        for (int index = 0; index < expected.Length; index++)
        {
            BasePreparedTextIndexEvidence actual = prepared.Indexes[index];
            var required = expected[index];
            if (actual.CollectionId != required.CollectionId || actual.TextIndexId != required.TextIndexId
                || actual.TextIndexVersion != required.TextIndexVersion || actual.CapturedGeneration <= 0
                || !Equal(actual.TextIndexChecksum, required.TextIndexChecksum)) return false;
        }
        return prepared.EvidenceBytes == checked(4L + prepared.ProjectionDigest.Length
            + prepared.Indexes.Sum(static value => 8L + Encoding.UTF8.GetByteCount(value.CollectionId) + Encoding.UTF8.GetByteCount(value.TextIndexId) + value.TextIndexChecksum.Length)
            + planned.Facts.Sum(static fact => 4L + fact.FactChecksum.Length));
    }

    internal static bool AppliedMatches(BaseFinalizedTextMutationExtension? planned, BasePreparedTextMutationEvidence? prepared, BaseAppliedTextMutationEvidence? applied, IReadOnlyList<BaseOwnedMutationFact> mutations)
    {
        if (planned is null) return prepared is null && applied is null;
        if (prepared is null || applied is null || applied.Facts.Length != planned.Facts.Length || applied.EvidenceDigest.Length != 32
            || applied.Indexes.Length != prepared.Indexes.Length
            || !applied.Indexes.Zip(prepared.Indexes).All(static pair => pair.First.CollectionId == pair.Second.CollectionId && pair.First.TextIndexId == pair.Second.TextIndexId && pair.First.TextIndexVersion == pair.Second.TextIndexVersion && pair.First.CapturedGeneration == pair.Second.CapturedGeneration && Equal(pair.First.TextIndexChecksum, pair.Second.TextIndexChecksum))) return false;
        BaseRecordMutationFact[] materialized;
        try { materialized = mutations.Select(static value => value.MaterializeOwned()).ToArray(); }
        catch { return false; }
        BaseAppliedTextMutationEvidence expected;
        try { expected = Apply(planned, materialized, applied.Indexes)!; }
        catch { return false; }
        return applied.EvidenceBytes == expected.EvidenceBytes && Equal(applied.EvidenceDigest, expected.EvidenceDigest)
            && applied.Facts.Select(static fact => fact.FactChecksum).Zip(expected.Facts.Select(static fact => fact.FactChecksum), Equal).All(static value => value);
    }

    private static BaseTextProjectionFact Fact(int ordinal, CollectionDefinition collection, BaseTextIndexDefinition index, RecordId recordId,
        RecordEnvelope? before, RecordPayload? after, OperationContext operation, bool applied) =>
        Fact(ordinal, collection, index, recordId, before, after, applied ? before?.Metadata.Revision : null, operation.TenantId, operation.ProjectId);

    private static BaseTextProjectionFact Fact(int ordinal, CollectionDefinition collection, BaseTextIndexDefinition index, RecordId recordId,
        RecordEnvelope? before, RecordPayload? after, RevisionToken? afterRevision, string? tenantId, string? projectId)
    {
        BaseTextProjectionRecordState? prior = before is null ? null : State(before.Payload, before.Metadata.Revision, index, collection, tenantId, projectId);
        BaseTextProjectionRecordState? next = after is null ? null : State(after, afterRevision, index, collection, tenantId, projectId);
        var value = new BaseTextProjectionFact
        {
            MutationOrdinal = ordinal, CollectionId = Copy(collection.Id), TextIndexId = Copy(index.Id), TextIndexVersion = index.Version,
            TextIndexChecksum = Copy(index.DefinitionChecksum), RecordId = new(Copy(recordId.Value)), Before = prior, After = next,
            Disposition = next is null ? BaseTextProjectionDisposition.Remove : BaseTextProjectionDisposition.Upsert,
            FactChecksum = [],
        };
        return value with { FactChecksum = DigestFact(value) };
    }

    private static BaseTextProjectionRecordState State(RecordPayload payload, RevisionToken? revision, BaseTextIndexDefinition index,
        CollectionDefinition collection, string? tenantId, string? projectId)
    {
        Dictionary<string, JsonElement> values = payload.Kind switch
        {
            RecordPayloadKind.FieldMap => (payload.Fields ?? []).ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal),
            RecordPayloadKind.Json when payload.Json.ValueKind == JsonValueKind.Object => payload.Json.EnumerateObject().ToDictionary(static value => value.Name, static value => value.Value.Clone(), StringComparer.Ordinal),
            _ => throw new InvalidOperationException(BaseTextErrorCodes.ProviderContractInvalid),
        };
        Dictionary<string, FieldDefinition> fields = (collection.Fields ?? []).ToDictionary(static value => value.Id, StringComparer.Ordinal);
        string[] ids = index.Fields.Select(static value => value.StableFieldId).Concat(index.FilterFields.Select(static value => value.StableFieldId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var projected = ImmutableArray.CreateBuilder<BaseTextProjectionFieldValue>(ids.Length);
        foreach (string id in ids)
        {
            if (!fields.TryGetValue(id, out FieldDefinition? field)) throw new InvalidOperationException(BaseTextErrorCodes.ProviderContractInvalid);
            if (!values.TryGetValue(field.WireName, out JsonElement value)) projected.Add(new() { StableFieldId = Copy(id), Missing = true, CanonicalJsonUtf8 = [] });
            else projected.Add(new() { StableFieldId = Copy(id), Missing = false, CanonicalJsonUtf8 = ImmutableArray.Create(Canonical(value)) });
        }
        ImmutableArray<BaseTextProjectionFieldValue> result = projected.MoveToImmutable();
        var state = new BaseTextProjectionRecordState { Revision = revision is null ? null : new(Copy(revision.Value.Value)), Fields = result, TenantId = CopyNullable(tenantId), ProjectId = CopyNullable(projectId), StateChecksum = [] };
        return state with { StateChecksum = DigestState(state) };
    }

    private static ImmutableArray<byte> DigestFacts(ImmutableArray<BaseTextProjectionFact> facts) => Hash(stream =>
    { Marker(stream, "HPDB-TEXT-PROJECTION-1"); U32(stream, facts.Length); foreach (BaseTextProjectionFact fact in facts) Bytes(stream, fact.FactChecksum.AsSpan()); });
    private static ImmutableArray<byte> DigestFact(BaseTextProjectionFact fact) => Hash(stream =>
    { Marker(stream, "HPDB-TEXT-PROJECTION-FACT-1"); U32(stream, fact.MutationOrdinal); String(stream, fact.CollectionId); String(stream, fact.TextIndexId); U32(stream, fact.TextIndexVersion); Bytes(stream, fact.TextIndexChecksum.AsSpan()); String(stream, fact.RecordId.Value); State(stream, fact.Before); State(stream, fact.After); stream.WriteByte((byte)fact.Disposition); });
    private static ImmutableArray<byte> DigestState(BaseTextProjectionRecordState state) => Hash(stream =>
    { Marker(stream, "HPDB-TEXT-PROJECTION-STATE-1"); OptionalString(stream, state.Revision?.Value); OptionalString(stream, state.TenantId); OptionalString(stream, state.ProjectId); U32(stream, state.Fields.Length); foreach (BaseTextProjectionFieldValue field in state.Fields) { String(stream, field.StableFieldId); stream.WriteByte(field.Missing ? (byte)1 : (byte)0); Bytes(stream, field.CanonicalJsonUtf8.AsSpan()); } });
    private static void State(Stream stream, BaseTextProjectionRecordState? value) { stream.WriteByte(value is null ? (byte)0 : (byte)1); if (value is not null) { OptionalString(stream, value.Revision?.Value); OptionalString(stream, value.TenantId); OptionalString(stream, value.ProjectId); U32(stream, value.Fields.Length); foreach (BaseTextProjectionFieldValue field in value.Fields) { String(stream, field.StableFieldId); stream.WriteByte(field.Missing ? (byte)1 : (byte)0); Bytes(stream, field.CanonicalJsonUtf8.AsSpan()); } Bytes(stream, value.StateChecksum.AsSpan()); } }
    private static byte[] Canonical(JsonElement value) { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); value.WriteTo(writer); writer.Flush(); return buffer.WrittenSpan.ToArray(); }
    private static ImmutableArray<byte> Hash(Action<Stream> write) { using var stream = new MemoryStream(); write(stream); return ImmutableArray.Create(SHA256.HashData(stream.ToArray())); }
    private static void Marker(Stream stream, string value) { stream.Write(Encoding.ASCII.GetBytes(value)); stream.WriteByte(0); }
    private static void U32(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void String(Stream stream, string value) => Bytes(stream, Encoding.UTF8.GetBytes(value));
    private static void OptionalString(Stream stream, string? value) { stream.WriteByte(value is null ? (byte)0 : (byte)1); if (value is not null) String(stream, value); }
    private static void Bytes(Stream stream, ReadOnlySpan<byte> value) { U32(stream, value.Length); stream.Write(value); }
    private static bool Equal(ImmutableArray<byte> left, ImmutableArray<byte> right) => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());
    private static ImmutableArray<byte> Copy(ImmutableArray<byte> value) => ImmutableArray.Create(value.ToArray());
    private static string Copy(string value) => new(value.AsSpan());
    private static string? CopyNullable(string? value) => value is null ? null : Copy(value);
}
