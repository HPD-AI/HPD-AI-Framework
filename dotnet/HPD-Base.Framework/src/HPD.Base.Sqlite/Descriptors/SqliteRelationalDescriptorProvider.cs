using HPD.Base.Query;
using HPD.Base.Descriptors;
using HPD.Base.Relational.Capabilities;
using HPD.Base.Relational.Descriptors;
using HPD.Base.Relational.Planning;
using HPD.Base.Relational.Providers;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Results;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Internal;
using HPD.Base.Sqlite.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HPD.Base.Sqlite.Descriptors;

internal sealed class SqliteRelationalDescriptorProvider :
    IRelationalMetadataProvider,
    IRelationalCollectionMappingProvider,
    IRelationalQueryPlanExplainer
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteNames _names;

    public SqliteRelationalDescriptorProvider(IOptions<HPDBaseSqliteOptions> options)
    {
        _options = options.Value;
        _names = new SqliteNames(_options);
    }

    public ValueTask<OperationResult<RelationalStoreDescriptor>> GetStoreAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = new RelationalStoreDescriptor
        {
            Id = _options.StoreId + ".relational",
            StoreId = _options.StoreId,
            DescriptorVersion = _options.StoreVersion,
            Provider = new RelationalProviderDescriptor { Id = "sqlite", Name = "SQLite", EngineFamily = "sqlite", Version = _options.StoreVersion, Visibility = VisibilityLevel.Public, PublicSafe = true },
            Databases = [Database(visibility)],
            Schemas = [Schema(visibility)],
            Tables = Tables(visibility),
            Columns = Columns(visibility),
            PrimaryKeys = [PrimaryKey(visibility)],
            Indexes = [UpdatedIndex(visibility)],
            GeneratedColumns = [],
            JsonColumns = [JsonColumn(visibility)],
            CollectionMappings = Mappings(visibility),
            Visibility = visibility,
            PublicSafe = visibility == VisibilityLevel.Public,
            Extensions = Extensions(visibility)
        };
        return ValueTask.FromResult(OperationResults.Ok(store));
    }

    public ValueTask<OperationResult<RelationalTableDescriptor[]>> ListTablesAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(Tables(visibility)));
    }

    public ValueTask<OperationResult<RelationalViewDescriptor[]>> ListViewsAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(Array.Empty<RelationalViewDescriptor>()));
    }

    public ValueTask<OperationResult<RelationalCollectionMappingDescriptor?>> GetMappingAsync(CollectionDefinition collection, OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok<RelationalCollectionMappingDescriptor?>(Mapping(collection.Id, visibility)));
    }

    public ValueTask<OperationResult<RelationalCollectionMappingDescriptor[]>> ListMappingsAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(Mappings(visibility)));
    }

    public ValueTask<OperationResult<RelationalQueryPlanDescriptor>> ExplainAsync(CollectionDefinition collection, OperationContext context, RecordQuery query, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = new SqliteQueryPlanner(_options).Plan(collection.Id, query);
        var descriptor = new RelationalQueryPlanDescriptor
        {
            Id = _options.StoreId + ".plan." + collection.Id,
            StoreId = _options.StoreId,
            CollectionId = collection.Id,
            Status = plan.Supported ? RelationalQueryPlanStatus.Supported : RelationalQueryPlanStatus.Unsupported,
            ExecutableForRequestedContext = plan.Supported,
            SafeForRequestedContext = plan.Supported,
            Pushdown = new RelationalQueryPushdownDescriptor
            {
                Filter = plan.Supported ? RelationalPushdownSupport.Complete : RelationalPushdownSupport.Unsupported,
                Sort = plan.Supported ? RelationalPushdownSupport.Complete : RelationalPushdownSupport.Unsupported,
                Page = plan.Supported ? RelationalPushdownSupport.Complete : RelationalPushdownSupport.Unsupported,
                Count = plan.Supported ? RelationalPushdownSupport.Complete : RelationalPushdownSupport.Unsupported,
                Select = RelationalPushdownSupport.Partial,
                Include = RelationalPushdownSupport.Unsupported,
                CompleteBeforeObservableArtifacts = plan.Supported,
                UnsupportedParts = plan.UnsupportedParts,
                Visibility = visibility,
                PublicSafe = visibility == VisibilityLevel.Public
            },
            Residual = new RelationalResidualDescriptor { Kind = RelationalResidualKind.ClientSideDisallowed, Required = false, SafeForRequestedContext = plan.Supported, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            Count = new RelationalCountPlanDescriptor { Requested = query.Count != QueryCountMode.None, Mode = query.Count, ExactCandidateSet = plan.Supported, SafeForRequestedContext = plan.Supported, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            Page = new RelationalPagePlanDescriptor { Requested = query.Page is not null, Mode = query.Page?.Mode ?? QueryPaginationMode.Page, PageAppliedAfterAllRequiredFilters = plan.Supported, SafeForRequestedContext = plan.Supported, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            Sort = new RelationalSortPlanDescriptor { Requested = query.Sort is { Length: > 0 }, CompleteBeforePage = plan.Supported, SortKeysVisibleOrAuthorized = true, SafeForRequestedContext = plan.Supported, UnsupportedParts = plan.UnsupportedParts, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            UnsupportedParts = plan.UnsupportedParts,
            Diagnostics = plan.Supported ? null : [new RelationalPlanDiagnostic { Id = "sqlite.query.unsupported", Code = "sqlite.query.unsupported", Severity = RelationalPlanDiagnosticSeverity.Error, Message = "SQLite provider cannot safely execute one or more query parts before count/page.", Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public }],
            Visibility = visibility,
            PublicSafe = visibility == VisibilityLevel.Public
        };
        return ValueTask.FromResult(OperationResults.Ok(descriptor));
    }

    private RelationalDatabaseDescriptor Database(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".database.main",
        StoreId = _options.StoreId,
        NativeName = "main",
        NativePath = visibility == VisibilityLevel.Admin ? NativeDataSource() : null,
        Kind = RelationalNamespaceKind.Database,
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalSchemaDescriptor Schema(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".schema.main",
        StoreId = _options.StoreId,
        NativeName = "main",
        NativePath = visibility == VisibilityLevel.Admin ? "main" : null,
        DatabaseRef = _options.StoreId + ".database.main",
        Kind = RelationalNamespaceKind.ProviderNamespace,
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalTableDescriptor[] Tables(VisibilityLevel visibility) =>
    [
        Table(_names.Records, mapped: true, visibility),
        Table(_names.Collections, mapped: false, visibility),
        Table(_names.ProviderState, mapped: false, visibility)
    ];

    private RelationalTableDescriptor Table(string name, bool mapped, VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".table." + name,
        StoreId = _options.StoreId,
        NativeName = name,
        NativePath = visibility == VisibilityLevel.Admin ? "main." + name : null,
        DatabaseRef = _options.StoreId + ".database.main",
        SchemaRef = _options.StoreId + ".schema.main",
        MappedCollectionIds = mapped ? CollectionIds() : null,
        PrimaryKeyRef = mapped ? _options.StoreId + ".pk." + _names.Records : null,
        ColumnRefs = mapped ? ColumnIds() : null,
        IndexRefs = mapped ? [_options.StoreId + ".index." + _names.RecordsUpdatedIndex] : null,
        ReadSupported = true,
        WriteSupported = mapped,
        RowIdentityStrategy = mapped ? RelationalRecordIdMappingKind.CompositeKey : RelationalRecordIdMappingKind.NativePrimaryKey,
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalColumnDescriptor[] Columns(VisibilityLevel visibility)
    {
        var table = _options.StoreId + ".table." + _names.Records;
        return
        [
            Column(table, "collection_id", 0, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility),
            Column(table, "record_id", 1, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility),
            Column(table, "revision", 2, RelationalColumnTypeFamily.Integer, "INTEGER", false, true, visibility),
            Column(table, "created_at", 3, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility),
            Column(table, "updated_at", 4, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility),
            Column(table, "payload_json", 5, RelationalColumnTypeFamily.Json, "TEXT", false, false, visibility, jsonRef: _options.StoreId + ".json." + _names.Records + ".payload_json")
        ];
    }

    private RelationalColumnDescriptor Column(string table, string name, int ordinal, RelationalColumnTypeFamily family, string nativeType, bool nullable, bool system, VisibilityLevel visibility, string? jsonRef = null) => new()
    {
        Id = _options.StoreId + ".column." + _names.Records + "." + name,
        StoreId = _options.StoreId,
        ParentObjectRef = table,
        NativeName = name,
        Ordinal = ordinal,
        Type = new RelationalColumnTypeDescriptor { NativeTypeName = nativeType, Family = family },
        Nullable = nullable,
        System = system,
        JsonColumnRef = jsonRef,
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalPrimaryKeyDescriptor PrimaryKey(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".pk." + _names.Records,
        StoreId = _options.StoreId,
        TableRef = _options.StoreId + ".table." + _names.Records,
        ColumnRefs = [_options.StoreId + ".column." + _names.Records + ".collection_id", _options.StoreId + ".column." + _names.Records + ".record_id"],
        NativeName = "PRIMARY KEY",
        RecordIdMappingKind = RelationalRecordIdMappingKind.CompositeKey,
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalIndexDescriptor UpdatedIndex(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".index." + _names.RecordsUpdatedIndex,
        StoreId = _options.StoreId,
        ParentObjectRef = _options.StoreId + ".table." + _names.Records,
        NativeName = _names.RecordsUpdatedIndex,
        Parts =
        [
            new RelationalIndexPartDescriptor { Id = _options.StoreId + ".indexpart.collection", Ordinal = 0, ColumnRef = _options.StoreId + ".column." + _names.Records + ".collection_id", Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            new RelationalIndexPartDescriptor { Id = _options.StoreId + ".indexpart.updated", Ordinal = 1, ColumnRef = _options.StoreId + ".column." + _names.Records + ".updated_at", Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            new RelationalIndexPartDescriptor { Id = _options.StoreId + ".indexpart.record", Ordinal = 2, ColumnRef = _options.StoreId + ".column." + _names.Records + ".record_id", Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public }
        ],
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalJsonColumnDescriptor JsonColumn(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".json." + _names.Records + ".payload_json",
        StoreId = _options.StoreId,
        ColumnRef = _options.StoreId + ".column." + _names.Records + ".payload_json",
        StorageKind = RelationalJsonStorageKind.TextJson,
        QueryablePathsSupported = true,
        PathIndexSupported = false,
        PayloadRootFieldPath = "$",
        NullMissingSemanticsSummary = "Top-level JSON paths use SQLite json_extract/json_type semantics.",
        SerializationSummary = "Canonical HPD.BASE field-map JSON object stored as TEXT.",
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalCollectionMappingDescriptor[] Mappings(VisibilityLevel visibility) =>
        CollectionIds().Select(id => Mapping(id, visibility)).ToArray();

    private RelationalCollectionMappingDescriptor Mapping(string collectionId, VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".mapping." + collectionId,
        StoreId = _options.StoreId,
        CollectionId = collectionId,
        TableRef = _options.StoreId + ".table." + _names.Records,
        MappingKind = RelationalMappingKind.Table,
        RecordIdMappingKind = RelationalRecordIdMappingKind.CompositeKey,
        RecordIdColumnRefs = [_options.StoreId + ".column." + _names.Records + ".collection_id", _options.StoreId + ".column." + _names.Records + ".record_id"],
        RecordIdSummary = "BASE record id is record_id within collection_id.",
        PayloadMappingKind = RelationalPayloadMappingKind.JsonColumn,
        PayloadJsonColumnRef = _options.StoreId + ".json." + _names.Records + ".payload_json",
        RevisionColumnRef = _options.StoreId + ".column." + _names.Records + ".revision",
        CreatedAtColumnRef = _options.StoreId + ".column." + _names.Records + ".created_at",
        UpdatedAtColumnRef = _options.StoreId + ".column." + _names.Records + ".updated_at",
        ListSupported = true,
        GetSupported = true,
        CreateSupported = true,
        PatchSupported = true,
        ReplaceSupported = true,
        DeleteSupported = true,
        FieldMappingRefs = FieldMappingRefs(collectionId),
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private string[] CollectionIds() =>
        _options.CollectionIds.Concat((_options.Collections ?? []).Select(c => c.Id)).Distinct(StringComparer.Ordinal).ToArray();

    private string[] ColumnIds() => new[] { "collection_id", "record_id", "revision", "created_at", "updated_at", "payload_json" }.Select(name => _options.StoreId + ".column." + _names.Records + "." + name).ToArray();

    private string[] FieldMappingRefs(string collectionId) =>
    [
        _options.StoreId + ".fieldMapping." + collectionId + ".id",
        _options.StoreId + ".fieldMapping." + collectionId + ".revision",
        _options.StoreId + ".fieldMapping." + collectionId + ".createdAt",
        _options.StoreId + ".fieldMapping." + collectionId + ".updatedAt",
        _options.StoreId + ".fieldMapping." + collectionId + ".payload"
    ];

    private Dictionary<string, JsonElement> Extensions(VisibilityLevel visibility)
    {
        var fieldMappings = FieldMappings(visibility);
        var capabilities = new RelationalCapabilityDescriptor
        {
            Id = _options.StoreId + ".relational.capabilities",
            StoreId = _options.StoreId,
            Version = _options.StoreVersion,
            Metadata = new RelationalMetadataCapability
            {
                Status = CapabilityStatus.Available,
                StoreMetadata = true,
                NamespaceMetadata = true,
                TableMetadata = true,
                ViewMetadata = true,
                ColumnMetadata = true,
                Visibility = visibility
            },
            Mapping = new RelationalMappingCapability
            {
                Status = CapabilityStatus.Available,
                CollectionMappings = true,
                FieldMappings = true,
                JsonColumnMappings = true,
                RelationMappings = false,
                Visibility = visibility
            },
            QueryPlanning = new RelationalQueryPlanningCapability
            {
                Status = CapabilityStatus.Available,
                ExplainOnly = true,
                NativePushdownSummary = true,
                ResidualSafetyDiagnostics = true,
                IncludePlanningDiagnostics = true,
                CountPageSafetyDiagnostics = true,
                Visibility = visibility
            },
            Constraints = new RelationalConstraintCapability { Status = CapabilityStatus.Available, PrimaryKeys = true, Visibility = visibility },
            JoinsIncludes = new RelationalJoinIncludeCapability { Status = CapabilityStatus.Unavailable, NativeEngineSupportsJoins = true, CallableIncludeExecutionAvailable = false, Summary = "SQLite joins exist, but HPD.BASE SQLite L21 does not implement includes/joins.", Visibility = VisibilityLevel.Admin },
            Transactions = new RelationalTransactionCapability { Status = CapabilityStatus.Available, NativeEngineSupportsTransactions = true, CallableInterfaceAvailable = false, Summary = "Provider mutations use SQLite immediate write transactions; no public transaction API is exposed.", Visibility = VisibilityLevel.Admin },
            SchemaWrite = new RelationalSchemaWriteCapability { Status = CapabilityStatus.Unavailable, NativeEngineSupportsDefinitionChanges = true, CallableInterfaceAvailable = false, DefinitionChangeRunnerAvailable = false, Summary = "L21 only initializes provider-owned schema; no host schema mutation API is exposed.", Visibility = VisibilityLevel.Admin },
            NativePolicy = new RelationalNativePolicyCapability { Status = CapabilityStatus.Unavailable, NativePolicyMechanismKnown = false, CallablePolicyAdministrationAvailable = false, ProjectionExplainOnly = true, Summary = "Runtime policy composition is pushed down as ordinary BASE filters only.", Visibility = VisibilityLevel.Admin },
            Visibility = visibility
        };

        return new Dictionary<string, JsonElement>
        {
            ["schemaPrefix"] = JsonSerializer.SerializeToElement(_options.SchemaPrefix, HPDBaseSqliteJsonSerializerContext.Default.String),
            ["walRequested"] = JsonSerializer.SerializeToElement(_options.EnableWal, HPDBaseSqliteJsonSerializerContext.Default.Boolean),
            ["busyTimeoutMilliseconds"] = JsonSerializer.SerializeToElement((int)_options.BusyTimeout.TotalMilliseconds, HPDBaseSqliteJsonSerializerContext.Default.Int32),
            ["relationalCapabilities"] = JsonSerializer.SerializeToElement(capabilities, HPDBaseSqliteJsonSerializerContext.Default.RelationalCapabilityDescriptor),
            ["relationalFieldMappings"] = JsonSerializer.SerializeToElement(fieldMappings, HPDBaseSqliteJsonSerializerContext.Default.RelationalFieldMappingDescriptorArray)
        };
    }

    private RelationalFieldMappingDescriptor[] FieldMappings(VisibilityLevel visibility)
    {
        var mappings = new List<RelationalFieldMappingDescriptor>();
        foreach (var collectionId in CollectionIds())
        {
            mappings.Add(SystemFieldMapping(collectionId, "id", "record_id", RelationalColumnTypeFamily.Text, visibility));
            mappings.Add(SystemFieldMapping(collectionId, "revision", "revision", RelationalColumnTypeFamily.Integer, visibility));
            mappings.Add(SystemFieldMapping(collectionId, "createdAt", "created_at", RelationalColumnTypeFamily.Text, visibility));
            mappings.Add(SystemFieldMapping(collectionId, "updatedAt", "updated_at", RelationalColumnTypeFamily.Text, visibility));
            mappings.Add(new RelationalFieldMappingDescriptor
            {
                Id = _options.StoreId + ".fieldMapping." + collectionId + ".payload",
                StoreId = _options.StoreId,
                CollectionId = collectionId,
                FieldPath = "*",
                JsonColumnRef = _options.StoreId + ".json." + _names.Records + ".payload_json",
                JsonPath = "$",
                NativeType = new RelationalColumnTypeDescriptor { NativeTypeName = "TEXT", Family = RelationalColumnTypeFamily.Json },
                ConversionKind = RelationalFieldConversionKind.JsonSerialization,
                NullMissingSemanticsSummary = "Payload fields are top-level JSON properties; JSON null and missing are distinguished with json_type.",
                Visibility = visibility,
                PublicSafe = visibility == VisibilityLevel.Public
            });

            foreach (var field in (_options.Collections ?? []).FirstOrDefault(collection => string.Equals(collection.Id, collectionId, StringComparison.Ordinal))?.Fields ?? [])
            {
                mappings.Add(new RelationalFieldMappingDescriptor
                {
                    Id = _options.StoreId + ".fieldMapping." + collectionId + "." + field.Id,
                    StoreId = _options.StoreId,
                    CollectionId = collectionId,
                    FieldPath = field.Id,
                    JsonColumnRef = _options.StoreId + ".json." + _names.Records + ".payload_json",
                    JsonPath = "$." + field.Id,
                    NativeType = new RelationalColumnTypeDescriptor { NativeTypeName = "TEXT", Family = RelationalColumnTypeFamily.Json },
                    ConversionKind = RelationalFieldConversionKind.JsonSerialization,
                    NullMissingSemanticsSummary = "Declared payload field is stored under payload_json as a top-level JSON property.",
                    Visibility = visibility,
                    PublicSafe = visibility == VisibilityLevel.Public
                });
            }
        }

        return mappings.ToArray();
    }

    private RelationalFieldMappingDescriptor SystemFieldMapping(string collectionId, string fieldPath, string columnName, RelationalColumnTypeFamily family, VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".fieldMapping." + collectionId + "." + fieldPath,
        StoreId = _options.StoreId,
        CollectionId = collectionId,
        FieldPath = fieldPath,
        ColumnRef = _options.StoreId + ".column." + _names.Records + "." + columnName,
        NativeType = new RelationalColumnTypeDescriptor { NativeTypeName = family == RelationalColumnTypeFamily.Integer ? "INTEGER" : "TEXT", Family = family },
        WriteBehavior = RelationalColumnWriteBehavior.StoreGenerated,
        Visibility = visibility,
        PublicSafe = visibility == VisibilityLevel.Public
    };

    private string? NativeDataSource()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return new SqliteConnectionStringBuilder(_options.ConnectionString).DataSource;
        }

        return _options.DataSource;
    }
}
