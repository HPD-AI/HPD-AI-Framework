using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseLogicalSchemaFactory
{
    internal static BaseLogicalSchema Create(
        HPDBaseSchemaOptions options,
        IEnumerable<CollectionDefinition> collectionValues,
        IEnumerable<IBaseReadRegistration> readValues)
    {
        CollectionDefinition[] sourceCollections = collectionValues.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        IBaseReadRegistration[] sourceReads = readValues.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        BaseLogicalCollection[] collections = sourceCollections.Select(static collection => new BaseLogicalCollection
        {
            Id = collection.Id,
            Name = collection.Name,
        }).ToArray();
        BaseLogicalField[] fields = sourceCollections.SelectMany(static collection => (collection.Fields ?? []).Select(field => new BaseLogicalField
        {
            CollectionId = collection.Id,
            Id = field.Id,
            StoredName = field.Name,
            Type = field.Type,
            Required = field.Required,
            Nullable = field.Nullable,
        })).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        RelationDefinition[] relations = sourceCollections.SelectMany(static collection => collection.Fields ?? [])
            .Select(static field => field.Relation).Where(static relation => relation is not null).Cast<RelationDefinition>()
            .OrderBy(static relation => relation.Id, StringComparer.Ordinal).ToArray();
        BaseLogicalIndex[] indexes = sourceCollections.SelectMany(static collection => collection.Indexes ?? []).Select(static index => new BaseLogicalIndex
        {
            CollectionId = index.CollectionId,
            Id = index.Id,
            FieldIds = (index.Parts ?? []).Select(static part => part.FieldId!).ToArray(),
            Unique = index.Unique,
        }).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        BaseLogicalRead[] reads = sourceReads.Select(static read => new BaseLogicalRead
        {
            Id = read.Id,
            SourceIds = read.Plan.Sources.Select(static source => source.CollectionId).ToArray(),
            ProjectionFieldIds = read.Plan.Projection.Select(static projection => projection.FieldId).ToArray(),
        }).ToArray();

        string checksum = Checksum(options.ApplicationId, options.ContractVersion, collections, fields, relations, indexes, reads);
        return new BaseLogicalSchema
        {
            ApplicationId = options.ApplicationId,
            ContractVersion = options.ContractVersion,
            Collections = collections,
            Fields = fields,
            Relations = relations,
            Indexes = indexes,
            ReadDefinitions = reads,
            CanonicalChecksum = checksum,
        };
    }

    private static string Checksum(
        string applicationId, string contractVersion, BaseLogicalCollection[] collections,
        BaseLogicalField[] fields, RelationDefinition[] relations, BaseLogicalIndex[] indexes,
        BaseLogicalRead[] reads)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.logical-schema.v1"); Write(writer, applicationId); Write(writer, contractVersion);
        foreach (BaseLogicalCollection value in collections) { Write(writer, "collection"); Write(writer, value.Id); Write(writer, value.Name); }
        foreach (BaseLogicalField value in fields) { Write(writer, "field"); Write(writer, value.CollectionId); Write(writer, value.Id); Write(writer, value.StoredName); Write(writer, value.Type); Write(writer, value.Required); Write(writer, value.Nullable); }
        foreach (RelationDefinition value in relations)
        {
            Write(writer, "relation"); Write(writer, value.Id); Write(writer, value.SourceCollectionId); Write(writer, value.SourceFieldId);
            Write(writer, value.TargetCollectionId); Write(writer, value.TargetFieldId); Write(writer, (int)value.OwningSide);
            Write(writer, (int)value.LocalMultiplicity); Write(writer, (int)value.InverseMultiplicity); Write(writer, value.Required);
            Write(writer, value.Ordered); Write(writer, value.InverseNavigationId); Write(writer, (int)value.DeleteBehavior);
        }
        foreach (BaseLogicalIndex value in indexes) { Write(writer, "index"); Write(writer, value.CollectionId); Write(writer, value.Id); foreach (string field in value.FieldIds) Write(writer, field); Write(writer, value.Unique); }
        foreach (BaseLogicalRead value in reads) { Write(writer, "read"); Write(writer, value.Id); foreach (string source in value.SourceIds) Write(writer, source); foreach (string field in value.ProjectionFieldIds) Write(writer, field); }
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    private static void Write(ArrayBufferWriter<byte> writer, string? value)
    {
        if (value is null) { Write(writer, -1); return; }
        int count = Encoding.UTF8.GetByteCount(value); Write(writer, count);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(count)); writer.Advance(count);
    }
    private static void Write(ArrayBufferWriter<byte> writer, bool value) => Write(writer, value ? 1 : 0);
    private static void Write(ArrayBufferWriter<byte> writer, int value)
    { Span<byte> span = writer.GetSpan(sizeof(int)); BinaryPrimitives.WriteInt32BigEndian(span, value); writer.Advance(sizeof(int)); }
}
