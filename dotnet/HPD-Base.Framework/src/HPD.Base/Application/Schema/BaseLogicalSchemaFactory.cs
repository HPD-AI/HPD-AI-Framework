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
        IEnumerable<IBaseReadRegistration> readValues,
        BaseStorageProtectionGraph storageProtection)
    {
        CollectionDefinition[] sourceCollections = collectionValues.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        IBaseReadRegistration[] sourceReads = readValues.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        BaseLogicalCollection[] collections = sourceCollections.Select(static collection => new BaseLogicalCollection
        {
            Id = collection.Id,
            Name = collection.Name,
            System = collection.System,
            SystemOwnerModuleId = collection.SystemOwnerModuleId,
        }).ToArray();
        BaseLogicalField[] fields = sourceCollections.SelectMany(static collection => (collection.Fields ?? []).Select(field => new BaseLogicalField
        {
            CollectionId = collection.Id,
            Id = field.Id,
            StoredName = field.Name,
            Type = field.Type,
            Required = field.Required,
            Nullable = field.Nullable,
            Confidentiality = field.Confidentiality,
            Disclosure = BaseConfidentialityPolicy.Clone(field.Disclosure ?? BaseConfidentialityPolicy.Default(field.Confidentiality)),
            MaximumBytes = field.MaximumBytes,
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
        BaseLogicalVectorIndex[] vectorIndexes = sourceCollections
            .SelectMany(static collection => collection.VectorIndexes ?? [])
            .Select(static index => new BaseLogicalVectorIndex
            {
                CollectionId = index.CollectionId,
                Id = index.Id,
                VectorFieldId = index.VectorFieldId,
                VectorSpaceId = index.VectorSpaceId,
                Dimensions = index.Dimensions,
                Function = index.Function,
                FilterFieldIds = [.. index.FilterFieldIds.Order(StringComparer.Ordinal)],
            })
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .ToArray();
        BaseLogicalRead[] reads = sourceReads.Select(static read => new BaseLogicalRead
        {
            Id = read.Id,
            SourceIds = read.Plan.Sources.Select(static source => source.CollectionId).ToArray(),
            ProjectionFieldIds = read.Plan.Projection.Select(static projection => projection.FieldId).ToArray(),
        }).ToArray();

        string checksum = Checksum(options.ApplicationId, options.ContractVersion, collections, fields, relations, indexes, vectorIndexes, reads, storageProtection.Requirements);
        return new BaseLogicalSchema
        {
            ApplicationId = options.ApplicationId,
            ContractVersion = options.ContractVersion,
            Collections = collections,
            Fields = fields,
            Relations = relations,
            Indexes = indexes,
            VectorIndexes = vectorIndexes,
            ReadDefinitions = reads,
            CanonicalChecksum = checksum,
        };
    }

    private static string Checksum(
        string applicationId, string contractVersion, BaseLogicalCollection[] collections,
        BaseLogicalField[] fields, RelationDefinition[] relations, BaseLogicalIndex[] indexes,
        BaseLogicalVectorIndex[] vectorIndexes,
        BaseLogicalRead[] reads,
        BaseStorageProtectionRequirement[] storageRequirements)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.logical-schema.v1"); Write(writer, applicationId); Write(writer, contractVersion);
        foreach (BaseLogicalCollection value in collections) { Write(writer, "collection"); Write(writer, value.Id); Write(writer, value.Name); Write(writer, value.System); Write(writer, value.SystemOwnerModuleId); }
        foreach (BaseLogicalField value in fields)
        {
            Write(writer, "field"); Write(writer, value.CollectionId); Write(writer, value.Id); Write(writer, value.StoredName); Write(writer, value.Type); Write(writer, value.Required); Write(writer, value.Nullable);
            Write(writer, (int)value.Confidentiality); Write(writer, (int)value.Disclosure.RecordRead); Write(writer, (int)value.Disclosure.AuthoritativeHistory);
            Write(writer, (int)value.Disclosure.Event); Write(writer, (int)value.Disclosure.Realtime); Write(writer, (int)value.Disclosure.Diagnostic);
            Write(writer, (int)value.Disclosure.AuthoritativeBackup); Write(writer, (int)value.Disclosure.AdministrativeDataExport);
            Write(writer, (int)value.Disclosure.OrdinaryDataExport); Write(writer, (int)value.Disclosure.Indexing); Write(writer, value.MaximumBytes ?? -1);
        }
        foreach (RelationDefinition value in relations)
        {
            Write(writer, "relation"); Write(writer, value.Id); Write(writer, value.SourceCollectionId); Write(writer, value.SourceFieldId);
            Write(writer, value.TargetCollectionId); Write(writer, value.TargetFieldId); Write(writer, (int)value.OwningSide);
            Write(writer, (int)value.LocalMultiplicity); Write(writer, (int)value.InverseMultiplicity); Write(writer, value.Required);
            Write(writer, value.Ordered); Write(writer, value.InverseNavigationId); Write(writer, (int)value.DeleteBehavior);
        }
        foreach (BaseLogicalIndex value in indexes) { Write(writer, "index"); Write(writer, value.CollectionId); Write(writer, value.Id); foreach (string field in value.FieldIds) Write(writer, field); Write(writer, value.Unique); }
        foreach (BaseLogicalVectorIndex value in vectorIndexes)
        {
            Write(writer, "vector-index"); Write(writer, value.CollectionId); Write(writer, value.Id);
            Write(writer, value.VectorFieldId); Write(writer, value.VectorSpaceId); Write(writer, value.Dimensions);
            Write(writer, (int)value.Function); foreach (string field in value.FilterFieldIds) Write(writer, field);
        }
        foreach (BaseLogicalRead value in reads) { Write(writer, "read"); Write(writer, value.Id); foreach (string source in value.SourceIds) Write(writer, source); foreach (string field in value.ProjectionFieldIds) Write(writer, field); }
        foreach (BaseStorageProtectionRequirement value in storageRequirements.OrderBy(static item => item.OwningModuleId, StringComparer.Ordinal))
        {
            Write(writer, "storage-protection"); Write(writer, value.OwningModuleId);
            foreach (BaseStorageEncryptionGuarantee item in value.PermittedGuarantees) Write(writer, (int)item);
            foreach (BaseStorageKeyOwner item in value.PermittedKeyOwners) Write(writer, (int)item);
            Write(writer, (int)value.RequiredRotation); Write(writer, (int)value.MinimumVerification);
            foreach (System.Collections.Immutable.ImmutableArray<BaseStorageProtectionState> states in Coverage(value.Coverage))
            { Write(writer, states.Length); foreach (BaseStorageProtectionState state in states) Write(writer, (int)state); }
        }
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    private static IEnumerable<System.Collections.Immutable.ImmutableArray<BaseStorageProtectionState>> Coverage(BaseStorageProtectionCoverageRequirement value)
    {
        yield return value.AuthoritativeRecords; yield return value.Journal; yield return value.Receipts; yield return value.ProviderState; yield return value.Indexes;
        yield return value.TemporaryFiles; yield return value.AuthoritativeBackups; yield return value.AdministrativeExports; yield return value.OrdinaryExports; yield return value.ExternalFilesAndBlobs;
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
