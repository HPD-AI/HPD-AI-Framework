using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite;

internal sealed class SqliteRelationalDescriptorProvider :
    IRelationalMetadataProvider,
    IRelationalCollectionMappingProvider,
    IRelationalQueryPlanExplainer
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteNames _names;
    private readonly SqlitePhysicalModel _physical;

    /// <summary>Initializes a new instance.</summary>
    public SqliteRelationalDescriptorProvider(IOptions<HPDBaseSqliteOptions> options)
    {
        _options = options.Value;
        _names = new SqliteNames(_options);
        _physical = new SqlitePhysicalModel(_options);
    }

    /// <summary>Executes the get store async operation.</summary>
    public ValueTask<OperationResult<RelationalStoreDescriptor>> GetStoreAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(new RelationalStoreDescriptor
        {
            Id = _options.StoreId + ".relational",
            StoreId = _options.StoreId,
            DescriptorVersion = _options.StoreVersion,
            Provider = new RelationalProviderDescriptor { Id = "sqlite", Name = "SQLite", EngineFamily = "sqlite", Version = _options.StoreVersion, Visibility = VisibilityLevel.Public, PublicSafe = true },
            Databases = [Database(visibility)],
            Schemas = [Schema(visibility)],
            Tables = Tables(visibility),
            Columns = Columns(visibility),
            PrimaryKeys = _physical.Collections.Select(model => PrimaryKey(model, visibility)).ToArray(),
            Indexes = _physical.Collections.SelectMany(model => new[] { UpdatedIndex(model, visibility) }.Concat(model.Indexes.Select(index => DeclaredIndex(model, index, visibility)))).ToArray(),
            GeneratedColumns = [],
            JsonColumns = JsonColumns(visibility),
            CollectionMappings = Mappings(visibility),
            Visibility = visibility,
            PublicSafe = visibility == VisibilityLevel.Public,
            Extensions = Extensions(visibility)
        }));
    }

    /// <summary>Executes the list tables async operation.</summary>
    public ValueTask<OperationResult<RelationalTableDescriptor[]>> ListTablesAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(Tables(visibility)));
    }

    /// <summary>Executes the list views async operation.</summary>
    public ValueTask<OperationResult<RelationalViewDescriptor[]>> ListViewsAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(Array.Empty<RelationalViewDescriptor>()));
    }

    /// <summary>Executes the get mapping async operation.</summary>
    public ValueTask<OperationResult<RelationalCollectionMappingDescriptor?>> GetMappingAsync(CollectionDefinition collection, OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok<RelationalCollectionMappingDescriptor?>(Mapping(_physical.Collection(collection.Id), visibility)));
    }

    /// <summary>Executes the list mappings async operation.</summary>
    public ValueTask<OperationResult<RelationalCollectionMappingDescriptor[]>> ListMappingsAsync(OperationContext context, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(Mappings(visibility)));
    }

    /// <summary>Executes the explain async operation.</summary>
    public ValueTask<OperationResult<RelationalQueryPlanDescriptor>> ExplainAsync(CollectionDefinition collection, OperationContext context, RecordQuery query, VisibilityLevel visibility, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = new SqliteQueryPlanner(_options, _physical.Collection(collection.Id)).Plan(query);
        return ValueTask.FromResult(OperationResults.Ok(new RelationalQueryPlanDescriptor
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
        }));
    }

    private RelationalDatabaseDescriptor Database(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".database.main", StoreId = _options.StoreId, NativeName = "main",
        NativePath = visibility == VisibilityLevel.Admin ? NativeDataSource() : null,
        Kind = RelationalNamespaceKind.Database, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalSchemaDescriptor Schema(VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".schema.main", StoreId = _options.StoreId, NativeName = "main",
        NativePath = visibility == VisibilityLevel.Admin ? "main" : null,
        DatabaseRef = _options.StoreId + ".database.main", Kind = RelationalNamespaceKind.ProviderNamespace,
        Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalTableDescriptor[] Tables(VisibilityLevel visibility) =>
        _physical.Collections.Select(model => new RelationalTableDescriptor
        {
            Id = TableRef(model), StoreId = _options.StoreId, NativeName = model.Table,
            NativePath = visibility == VisibilityLevel.Admin ? "main." + model.Table : null,
            DatabaseRef = _options.StoreId + ".database.main", SchemaRef = _options.StoreId + ".schema.main",
            MappedCollectionIds = [model.Definition.Id], PrimaryKeyRef = PrimaryKeyRef(model),
            ColumnRefs = ColumnRefs(model), IndexRefs = new[] { IndexRef(model) }.Concat(model.Indexes.Select(index => DeclaredIndexRef(index))).ToArray(), ReadSupported = true, WriteSupported = true,
            RowIdentityStrategy = RelationalRecordIdMappingKind.NativePrimaryKey,
            Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
        }).Concat(new[]
        {
            InfrastructureTable(_names.Collections, visibility), InfrastructureTable(_names.ProviderState, visibility), InfrastructureTable(_names.MutationJournal, visibility)
        }).ToArray();

    private RelationalTableDescriptor InfrastructureTable(string name, VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".table." + name, StoreId = _options.StoreId, NativeName = name,
        NativePath = visibility == VisibilityLevel.Admin ? "main." + name : null,
        DatabaseRef = _options.StoreId + ".database.main", SchemaRef = _options.StoreId + ".schema.main",
        ReadSupported = true, WriteSupported = false, RowIdentityStrategy = RelationalRecordIdMappingKind.NativePrimaryKey,
        Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalColumnDescriptor[] Columns(VisibilityLevel visibility) => _physical.Collections.SelectMany(model =>
    {
        var columns = new List<RelationalColumnDescriptor>
        {
            Column(model, "record_id", 0, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility),
            Column(model, "revision", 1, RelationalColumnTypeFamily.Integer, "INTEGER", false, true, visibility),
            Column(model, "created_at", 2, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility),
            Column(model, "updated_at", 3, RelationalColumnTypeFamily.Text, "TEXT", false, true, visibility)
        };
        var ordinal = 4;
        foreach (SqlitePhysicalModel.FieldModel field in model.Fields)
        {
            if (field.PresenceColumn is not null)
                columns.Add(Column(model, field.PresenceColumn, ordinal++, RelationalColumnTypeFamily.Integer, "INTEGER", false, true, visibility));
            columns.Add(Column(model, field.Column, ordinal++, Family(field), field.SqlType, field.PresenceColumn is not null, false, visibility));
        }
        if (model.HasExtensionJson)
            columns.Add(Column(model, "extension_json", ordinal, RelationalColumnTypeFamily.Json, "TEXT", true, false, visibility, JsonRef(model)));
        return columns;
    }).ToArray();

    private RelationalColumnDescriptor Column(SqlitePhysicalModel.CollectionModel model, string name, int ordinal, RelationalColumnTypeFamily family, string nativeType, bool nullable, bool system, VisibilityLevel visibility, string? jsonRef = null) => new()
    {
        Id = ColumnRef(model, name), StoreId = _options.StoreId, ParentObjectRef = TableRef(model), NativeName = name,
        Ordinal = ordinal, Type = new RelationalColumnTypeDescriptor { NativeTypeName = nativeType, Family = family },
        Nullable = nullable, System = system, JsonColumnRef = jsonRef, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalPrimaryKeyDescriptor PrimaryKey(SqlitePhysicalModel.CollectionModel model, VisibilityLevel visibility) => new()
    {
        Id = PrimaryKeyRef(model), StoreId = _options.StoreId, TableRef = TableRef(model),
        ColumnRefs = [ColumnRef(model, "record_id")], NativeName = "PRIMARY KEY",
        RecordIdMappingKind = RelationalRecordIdMappingKind.NativePrimaryKey, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalIndexDescriptor UpdatedIndex(SqlitePhysicalModel.CollectionModel model, VisibilityLevel visibility) => new()
    {
        Id = IndexRef(model), StoreId = _options.StoreId, ParentObjectRef = TableRef(model), NativeName = "ix_" + model.Table + "_updated",
        Parts =
        [
            new RelationalIndexPartDescriptor { Id = IndexRef(model) + ".part.updated", Ordinal = 0, ColumnRef = ColumnRef(model, "updated_at"), Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public },
            new RelationalIndexPartDescriptor { Id = IndexRef(model) + ".part.record", Ordinal = 1, ColumnRef = ColumnRef(model, "record_id"), Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public }
        ],
        Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalIndexDescriptor DeclaredIndex(SqlitePhysicalModel.CollectionModel model, SqlitePhysicalModel.IndexModel index, VisibilityLevel visibility) => new()
    {
        Id = DeclaredIndexRef(index), StoreId = _options.StoreId, ParentObjectRef = TableRef(model), NativeName = index.Name,
        Unique = index.Definition.Unique,
        Parts = index.Parts.Select((field, ordinal) => new RelationalIndexPartDescriptor
        {
            Id = DeclaredIndexRef(index) + ".part." + ordinal, Ordinal = ordinal, ColumnRef = ColumnRef(model, field.Column),
            SortDirection = index.Definition.Parts[ordinal].Direction == BaseIndexSortDirection.Descending ? "desc" : "asc",
            Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
        }).ToArray(),
        Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalJsonColumnDescriptor[] JsonColumns(VisibilityLevel visibility) => _physical.Collections.Where(model => model.HasExtensionJson).Select(model => new RelationalJsonColumnDescriptor
    {
        Id = JsonRef(model), StoreId = _options.StoreId, ColumnRef = ColumnRef(model, "extension_json"),
        StorageKind = RelationalJsonStorageKind.TextJson, QueryablePathsSupported = false, PathIndexSupported = false,
        PayloadRootFieldPath = "$", NullMissingSemanticsSummary = "Only undeclared preserved fields are stored in extension JSON.",
        SerializationSummary = "Canonical HPD.BASE extension field-map JSON stored as TEXT.", Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    }).ToArray();

    private RelationalCollectionMappingDescriptor[] Mappings(VisibilityLevel visibility) => _physical.Collections.Select(model => Mapping(model, visibility)).ToArray();

    private RelationalCollectionMappingDescriptor Mapping(SqlitePhysicalModel.CollectionModel model, VisibilityLevel visibility) => new()
    {
        Id = _options.StoreId + ".mapping." + model.Definition.Id, StoreId = _options.StoreId, CollectionId = model.Definition.Id,
        TableRef = TableRef(model), MappingKind = RelationalMappingKind.Table, RecordIdMappingKind = RelationalRecordIdMappingKind.NativePrimaryKey,
        RecordIdColumnRefs = [ColumnRef(model, "record_id")], RecordIdSummary = "BASE record id is the table primary key.",
        PayloadMappingKind = model.HasExtensionJson ? RelationalPayloadMappingKind.Hybrid : RelationalPayloadMappingKind.Columns,
        PayloadJsonColumnRef = model.HasExtensionJson ? JsonRef(model) : null,
        RevisionColumnRef = ColumnRef(model, "revision"), CreatedAtColumnRef = ColumnRef(model, "created_at"), UpdatedAtColumnRef = ColumnRef(model, "updated_at"),
        ListSupported = true, GetSupported = true, CreateSupported = true, PatchSupported = true, ReplaceSupported = true, DeleteSupported = true,
        FieldMappingRefs = FieldMappings(model, visibility).Select(mapping => mapping.Id).ToArray(),
        Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private RelationalFieldMappingDescriptor[] FieldMappings(VisibilityLevel visibility) => _physical.Collections.SelectMany(model => FieldMappings(model, visibility)).ToArray();

    private RelationalFieldMappingDescriptor[] FieldMappings(SqlitePhysicalModel.CollectionModel model, VisibilityLevel visibility)
    {
        var mappings = new List<RelationalFieldMappingDescriptor>
        {
            SystemField(model, "id", "record_id", RelationalColumnTypeFamily.Text, visibility),
            SystemField(model, "revision", "revision", RelationalColumnTypeFamily.Integer, visibility),
            SystemField(model, "createdAt", "created_at", RelationalColumnTypeFamily.Text, visibility),
            SystemField(model, "updatedAt", "updated_at", RelationalColumnTypeFamily.Text, visibility)
        };
        mappings.AddRange(model.Fields.Select(field => new RelationalFieldMappingDescriptor
        {
            Id = FieldMappingRef(model, field.Definition.Id), StoreId = _options.StoreId, CollectionId = model.Definition.Id,
            FieldPath = field.Definition.Id, ColumnRef = ColumnRef(model, field.Column),
            NativeType = new RelationalColumnTypeDescriptor { NativeTypeName = field.SqlType, Family = Family(field) },
            ConversionKind = RelationalFieldConversionKind.BaseTypeConversion,
            NullMissingSemanticsSummary = field.PresenceColumn is null ? "Required field stored directly." : "Presence column distinguishes missing from explicit null.",
            Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
        }));
        return mappings.ToArray();
    }

    private RelationalFieldMappingDescriptor SystemField(SqlitePhysicalModel.CollectionModel model, string path, string column, RelationalColumnTypeFamily family, VisibilityLevel visibility) => new()
    {
        Id = FieldMappingRef(model, path), StoreId = _options.StoreId, CollectionId = model.Definition.Id, FieldPath = path,
        ColumnRef = ColumnRef(model, column), NativeType = new RelationalColumnTypeDescriptor { NativeTypeName = family == RelationalColumnTypeFamily.Integer ? "INTEGER" : "TEXT", Family = family },
        WriteBehavior = RelationalColumnWriteBehavior.StoreGenerated, Visibility = visibility, PublicSafe = visibility == VisibilityLevel.Public
    };

    private Dictionary<string, JsonElement> Extensions(VisibilityLevel visibility)
    {
        var capabilities = new RelationalCapabilityDescriptor
        {
            Id = _options.StoreId + ".relational.capabilities", StoreId = _options.StoreId, Version = _options.StoreVersion,
            Metadata = new RelationalMetadataCapability { Status = CapabilityStatus.Available, StoreMetadata = true, NamespaceMetadata = true, TableMetadata = true, ViewMetadata = true, ColumnMetadata = true, Visibility = visibility },
            Mapping = new RelationalMappingCapability { Status = CapabilityStatus.Available, CollectionMappings = true, FieldMappings = true, JsonColumnMappings = _physical.Collections.Any(model => model.HasExtensionJson), RelationMappings = true, Visibility = visibility },
            QueryPlanning = new RelationalQueryPlanningCapability { Status = CapabilityStatus.Available, ExplainOnly = true, NativePushdownSummary = true, ResidualSafetyDiagnostics = true, IncludePlanningDiagnostics = true, CountPageSafetyDiagnostics = true, Visibility = visibility },
            Constraints = new RelationalConstraintCapability { Status = CapabilityStatus.Available, PrimaryKeys = true, Visibility = visibility },
            JoinsIncludes = new RelationalJoinIncludeCapability { Status = CapabilityStatus.Available, NativeEngineSupportsJoins = true, IncludePlanExplanationSupported = true, CallableIncludeExecutionAvailable = true, Summary = "Registered relational reads and bounded snapshot-consistent includes are callable.", Visibility = VisibilityLevel.Admin },
            Transactions = new RelationalTransactionCapability { Status = CapabilityStatus.Available, NativeEngineSupportsTransactions = true, CallableInterfaceAvailable = true, Summary = "Restricted provider transaction sessions execute canonical single and atomic mutations.", Visibility = VisibilityLevel.Admin },
            SchemaWrite = new RelationalSchemaWriteCapability { Status = CapabilityStatus.Available, NativeEngineSupportsDefinitionChanges = true, CallableInterfaceAvailable = true, DefinitionChangeRunnerAvailable = true, Summary = "Verified protected schema plans are callable through the BASE schema lifecycle.", Visibility = VisibilityLevel.Admin },
            NativePolicy = new RelationalNativePolicyCapability { Status = CapabilityStatus.Unavailable, NativePolicyMechanismKnown = false, CallablePolicyAdministrationAvailable = false, ProjectionExplainOnly = true, Summary = "Runtime policy evaluation remains authoritative.", Visibility = VisibilityLevel.Admin },
            Visibility = visibility
        };
        return new Dictionary<string, JsonElement>
        {
            ["schemaPrefix"] = JsonSerializer.SerializeToElement(_options.SchemaPrefix, HPDBaseSqliteJsonSerializerContext.Default.String),
            ["walRequested"] = JsonSerializer.SerializeToElement(_options.EnableWal, HPDBaseSqliteJsonSerializerContext.Default.Boolean),
            ["busyTimeoutMilliseconds"] = JsonSerializer.SerializeToElement((int)_options.BusyTimeout.TotalMilliseconds, HPDBaseSqliteJsonSerializerContext.Default.Int32),
            ["relationalCapabilities"] = JsonSerializer.SerializeToElement(capabilities, HPDBaseSqliteJsonSerializerContext.Default.RelationalCapabilityDescriptor),
            ["relationalFieldMappings"] = JsonSerializer.SerializeToElement(FieldMappings(visibility), HPDBaseSqliteJsonSerializerContext.Default.RelationalFieldMappingDescriptorArray)
        };
    }

    private string TableRef(SqlitePhysicalModel.CollectionModel model) => _options.StoreId + ".table." + model.Table;
    private string ColumnRef(SqlitePhysicalModel.CollectionModel model, string column) => _options.StoreId + ".column." + model.Table + "." + column;
    private string[] ColumnRefs(SqlitePhysicalModel.CollectionModel model) => Columns(VisibilityLevel.Admin).Where(column => column.ParentObjectRef == TableRef(model)).Select(column => column.Id).ToArray();
    private string PrimaryKeyRef(SqlitePhysicalModel.CollectionModel model) => _options.StoreId + ".pk." + model.Table;
    private string IndexRef(SqlitePhysicalModel.CollectionModel model) => _options.StoreId + ".index.ix_" + model.Table + "_updated";
    private string DeclaredIndexRef(SqlitePhysicalModel.IndexModel index) => _options.StoreId + ".index." + index.Name;
    private string JsonRef(SqlitePhysicalModel.CollectionModel model) => _options.StoreId + ".json." + model.Table + ".extension_json";
    private string FieldMappingRef(SqlitePhysicalModel.CollectionModel model, string field) => _options.StoreId + ".fieldMapping." + model.Definition.Id + "." + field;
    private static RelationalColumnTypeFamily Family(SqlitePhysicalModel.FieldModel field) => field.SqlType switch { "INTEGER" => RelationalColumnTypeFamily.Integer, "REAL" => RelationalColumnTypeFamily.Decimal, _ when field.Definition.Type is BaseFieldTypes.Object or BaseFieldTypes.Array => RelationalColumnTypeFamily.Json, _ => RelationalColumnTypeFamily.Text };

    private string? NativeDataSource() => !string.IsNullOrWhiteSpace(_options.ConnectionString)
        ? new SqliteConnectionStringBuilder(_options.ConnectionString).DataSource
        : _options.DataSource;
}
