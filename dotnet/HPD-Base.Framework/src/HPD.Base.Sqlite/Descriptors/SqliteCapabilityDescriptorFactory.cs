using HPD.Base.Descriptors;
using HPD.Base.Query;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Stores;

namespace HPD.Base.Sqlite.Descriptors;

internal static class SqliteCapabilityDescriptorFactory
{
    public static CapabilityDescriptor Create(HPDBaseSqliteOptions options, StoreCapabilityDescriptor storeCapabilities) => new()
    {
        DescriptorVersion = options.StoreVersion,
        RuntimeId = options.ModuleId,
        Families =
        [
            new CapabilityFamilyDescriptor
            {
                FamilyId = BaseCapabilityFamilies.Store,
                FamilyVersion = options.StoreVersion,
                Status = CapabilityStatus.Available,
                OwnerModuleId = options.ModuleId,
                Features = StoreFeatures(options, storeCapabilities)
            },
            new CapabilityFamilyDescriptor
            {
                FamilyId = BaseCapabilityFamilies.Query,
                FamilyVersion = options.StoreVersion,
                Status = CapabilityStatus.Available,
                OwnerModuleId = options.ModuleId,
                Features =
                [
                    Feature(BaseFeatureIds.RecordsQuery, options, new CapabilityConstraintSet
                    {
                        QueryFilter = new QueryFilterCapabilityConstraints
                        {
                            Operators = storeCapabilities.Query.Filter.Operators,
                            BooleanComposition = storeCapabilities.Query.Filter.BooleanComposition,
                            Not = storeCapabilities.Query.Filter.Not,
                            NullChecks = storeCapabilities.Query.Filter.NullChecks,
                            MissingFieldChecks = storeCapabilities.Query.Filter.MissingFieldChecks,
                            NestedFieldPaths = storeCapabilities.Query.Filter.NestedFieldPaths,
                            ArrayMembership = storeCapabilities.Query.Filter.ArrayMembership,
                            MaxDepth = storeCapabilities.Query.Filter.MaxDepth,
                            MaxNodes = storeCapabilities.Query.Filter.MaxNodes,
                            MaxSerializedLength = storeCapabilities.Query.Filter.MaxSerializedLength,
                            ExecutionMode = storeCapabilities.Query.Filter.ExecutionMode
                        },
                        QuerySort = new QuerySortCapabilityConstraints
                        {
                            MaxFields = storeCapabilities.Query.Sort.MaxFields,
                            NestedFieldPaths = storeCapabilities.Query.Sort.NestedFieldPaths,
                            NullOrdering = storeCapabilities.Query.Sort.NullOrdering,
                            StableTieBreaker = storeCapabilities.Query.Sort.StableTieBreaker,
                            DefaultSort = storeCapabilities.Query.Sort.DefaultSort
                        },
                        QueryPagination = new QueryPaginationCapabilityConstraints
                        {
                            Page = storeCapabilities.Query.Pagination.Page,
                            Offset = storeCapabilities.Query.Pagination.Offset,
                            Cursor = storeCapabilities.Query.Pagination.Cursor,
                            DefaultLimit = storeCapabilities.Query.Pagination.DefaultLimit,
                            MaxLimit = storeCapabilities.Query.Pagination.MaxLimit,
                            CursorRequiresStableSort = storeCapabilities.Query.Pagination.CursorRequiresStableSort
                        },
                        QueryCount = new QueryCountCapabilityConstraints
                        {
                            SupportedModes = storeCapabilities.Query.Count.SupportedModes,
                            CountMayBeExpensive = storeCapabilities.Query.Count.CountMayBeExpensive
                        },
                        QuerySelect = new QuerySelectCapabilityConstraints
                        {
                            PayloadFields = storeCapabilities.Query.Select.PayloadFields,
                            SystemFields = storeCapabilities.Query.Select.SystemFields,
                            NestedFieldPaths = storeCapabilities.Query.Select.NestedFieldPaths
                        }
                    })
                ]
            }
        ]
    };

    private static CapabilityFeatureDescriptor[] StoreFeatures(
        HPDBaseSqliteOptions options,
        StoreCapabilityDescriptor capabilities)
    {
        var features = new List<CapabilityFeatureDescriptor>
        {
            Feature(BaseFeatureIds.RecordsList, options, new CapabilityConstraintSet { StoreRead = Read(capabilities) }),
            Feature(BaseFeatureIds.RecordsGet, options, new CapabilityConstraintSet { StoreRead = Read(capabilities) }),
            Feature(BaseFeatureIds.RecordsCreate, options, new CapabilityConstraintSet { StoreMutation = Mutation(capabilities) }),
            Feature(BaseFeatureIds.RecordsPatch, options, new CapabilityConstraintSet { StoreMutation = Mutation(capabilities), StoreRevision = Revision(capabilities) }),
            Feature(BaseFeatureIds.RecordsReplace, options, new CapabilityConstraintSet { StoreMutation = Mutation(capabilities), StoreRevision = Revision(capabilities) }),
            Feature(BaseFeatureIds.RecordsDelete, options, new CapabilityConstraintSet { StoreMutation = Mutation(capabilities), StoreRevision = Revision(capabilities) }),
            Feature(BaseFeatureIds.RecordsRevision, options, new CapabilityConstraintSet { StoreRevision = Revision(capabilities) }),
            Feature(BaseFeatureIds.StoreBatchAtomic, options, new CapabilityConstraintSet { Batch = Batch(capabilities) }),
            Feature(BaseFeatureIds.StoreBatchCrossCollection, options, new CapabilityConstraintSet { Batch = Batch(capabilities) })
        };
        if (capabilities.Upsert is not null)
        {
            features.Add(Feature(
                BaseFeatureIds.StoreRecordUpsertAtomic,
                options,
                new CapabilityConstraintSet { Upsert = Upsert(capabilities) }));
        }

        return features.ToArray();
    }

    private static CapabilityFeatureDescriptor Feature(string featureId, HPDBaseSqliteOptions options, CapabilityConstraintSet constraints) => new()
    {
        FeatureId = featureId,
        Version = options.StoreVersion,
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Required,
        Scope = CapabilityScope.Collection,
        AppliesTo = options.CollectionIds.Length == 0 ? null : options.CollectionIds,
        Constraints = constraints,
        HealthRef = options.ContributeHealth ? options.HealthRefId : null,
        DiagnosticRefs = options.ContributeDiagnostics ? [options.DiagnosticRefId] : null,
        Visibility = VisibilityLevel.Admin
    };

    private static StoreReadCapabilityConstraints Read(StoreCapabilityDescriptor capabilities) => new()
    {
        Operations = ["list", "get"],
        MaxPageSize = capabilities.Read.MaxPageSize
    };

    private static StoreMutationCapabilityConstraints Mutation(
        StoreCapabilityDescriptor capabilities) => new()
    {
        Operations = ["create", "patch", "replace", "delete"],
        IdAuthority = capabilities.Mutation.IdAuthority,
        TimestampAuthority = capabilities.Mutation.TimestampAuthority,
        Consistency = capabilities.Mutation.Consistency
    };

    private static StoreRevisionCapabilityConstraints? Revision(StoreCapabilityDescriptor capabilities) =>
        capabilities.Revision is null
            ? null
            : new StoreRevisionCapabilityConstraints
            {
                Patch = capabilities.Revision.Patch,
                Replace = capabilities.Revision.Replace,
                Delete = capabilities.Revision.Delete,
                Guarantee = capabilities.Revision.Guarantee
            };

    private static BatchCapabilityConstraints? Batch(StoreCapabilityDescriptor capabilities) =>
        capabilities.Batch is null
            ? null
            : new BatchCapabilityConstraints
            {
                Modes = capabilities.Batch.Modes,
                MaxOperations = capabilities.Batch.MaxOperations,
                MaxCanonicalPayloadBytes = capabilities.Batch.MaxCanonicalPayloadBytes,
                Ordered = capabilities.Batch.Ordered,
                PartialResults = capabilities.Batch.PartialResults,
                CrossCollectionAtomic = capabilities.Batch.CrossCollectionAtomic,
                ReadYourWrites = capabilities.Batch.ReadYourWrites,
                Durable = capabilities.Batch.Durable,
                TransactionalJournal = capabilities.Batch.TransactionalJournal,
                Isolation = capabilities.Batch.Isolation,
                NestedTransactions = capabilities.Batch.NestedTransactions,
                Savepoints = capabilities.Batch.Savepoints
            };

    private static UpsertCapabilityConstraints? Upsert(StoreCapabilityDescriptor capabilities) =>
        capabilities.Upsert is null
            ? null
            : new UpsertCapabilityConstraints
            {
                Atomic = capabilities.Upsert.Atomic,
                UpdateModes = capabilities.Upsert.UpdateModes,
                ExpectedRevision = capabilities.Upsert.ExpectedRevision,
                ExistenceConditions = capabilities.Upsert.ExistenceConditions
            };
}
