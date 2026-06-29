using HPD.Base.Descriptors;
using HPD.Base.InMemory.Configuration;
using HPD.Base.Query;
using HPD.Base.Stores;

namespace HPD.Base.InMemory.Descriptors;

internal static class InMemoryCapabilityDescriptorFactory
{
    public static CapabilityDescriptor Create(
        HPDBaseInMemoryOptions options,
        StoreCapabilityDescriptor storeCapabilities) => new()
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
                Features =
                [
                    Feature(BaseFeatureIds.RecordsList, options, new CapabilityConstraintSet { StoreCrud = Crud(options, storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsGet, options, new CapabilityConstraintSet { StoreCrud = Crud(options, storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsCreate, options, new CapabilityConstraintSet { StoreCrud = Crud(options, storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsPatch, options, new CapabilityConstraintSet { StoreCrud = Crud(options, storeCapabilities), StoreRevision = Revision(storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsReplace, options, new CapabilityConstraintSet { StoreCrud = Crud(options, storeCapabilities), StoreRevision = Revision(storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsDelete, options, new CapabilityConstraintSet { StoreCrud = Crud(options, storeCapabilities), StoreRevision = Revision(storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsRevision, options, new CapabilityConstraintSet { StoreRevision = Revision(storeCapabilities) }),
                    Feature(BaseFeatureIds.RecordsStreaming, options, new CapabilityConstraintSet { StoreStreaming = Streaming(storeCapabilities) })
                ]
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

    private static CapabilityFeatureDescriptor Feature(
        string featureId,
        HPDBaseInMemoryOptions options,
        CapabilityConstraintSet constraints) => new()
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

    private static StoreCrudCapabilityConstraints Crud(
        HPDBaseInMemoryOptions options,
        StoreCapabilityDescriptor capabilities) => new()
    {
        Operations =
        [
            "list",
            "get",
            "create",
            "patch",
            "replace",
            "delete"
        ],
        IdAuthority = capabilities.Crud.IdAuthority,
        TimestampAuthority = capabilities.Crud.TimestampAuthority,
        Consistency = capabilities.Crud.Consistency,
        MaxPageSize = options.MaxPageSize
    };

    private static StoreRevisionCapabilityConstraints? Revision(StoreCapabilityDescriptor capabilities) =>
        capabilities.Revision is null
            ? null
            : new StoreRevisionCapabilityConstraints
            {
                Patch = capabilities.Revision.Patch,
                Delete = capabilities.Revision.Delete,
                Guarantee = capabilities.Revision.Guarantee
            };

    private static StoreStreamingCapabilityConstraints? Streaming(StoreCapabilityDescriptor capabilities) =>
        capabilities.Streaming is null
            ? null
            : new StoreStreamingCapabilityConstraints
            {
                MaxItems = capabilities.Streaming.MaxItems,
                RequiresStableSort = capabilities.Streaming.RequiresStableSort
            };
}
