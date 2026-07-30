using HPD.Base.Schema;

namespace HPD.Base.Sqlite.Configuration;

/// <summary>Configures the durable HPD.BASE SQLite record store.</summary>
public sealed class HPDBaseSqliteOptions
{
    public string StoreId { get; set; } = HPDBaseSqliteDefaults.DefaultStoreId;
    public string ModuleId { get; set; } = HPDBaseSqliteDefaults.DefaultModuleId;
    public string ModuleName { get; set; } = HPDBaseSqliteDefaults.DefaultModuleName;
    public string StoreVersion { get; set; } = HPDBaseSqliteDefaults.DefaultStoreVersion;

    /// <summary>
    /// Gets or sets the exact SQLite connection string to use.
    /// </summary>
    /// <remarks>
    /// This value wins over <see cref="DataSource"/>. Use it when another host or integration package
    /// has already resolved the full connection string, such as an Aspire connection string resource.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the SQLite data source used to build a connection string when <see cref="ConnectionString"/> is not set.
    /// </summary>
    /// <remarks>
    /// This is the SQLite <c>Data Source</c> value, such as a database file path or <c>:memory:</c>.
    /// It is not a directory path.
    /// </remarks>
    public string? DataSource { get; set; }

    public string SchemaPrefix { get; set; } = HPDBaseSqliteDefaults.DefaultSchemaPrefix;
    public bool AutoInitialize { get; set; } = true;
    public bool FailIfSchemaMissing { get; set; }
    public bool EnableWal { get; set; } = true;
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int DefaultPageSize { get; set; } = 100;
    public int MaxPageSize { get; set; } = 1_000;
    public int MaxFilterDepth { get; set; } = 8;
    public int MaxFilterNodes { get; set; } = 128;
    public int MaxInValues { get; set; } = 100;
    public int MaxSortFields { get; set; } = 8;
    public int MaxSelectFields { get; set; } = 64;
    public bool AllowClientRequestedIds { get; set; } = true;
    public bool ContributeModuleDescriptor { get; set; } = true;
    public bool ContributeCapabilities { get; set; } = true;
    public bool ContributeHealth { get; set; } = true;
    public bool ContributeDiagnostics { get; set; } = true;
    public bool ContributeRelationalDescriptors { get; set; } = true;
    public bool InitializeSQLitePCLRaw { get; set; } = true;
    public TimeSpan MutationJournalRetention { get; set; } = TimeSpan.FromDays(7);
    public int MutationJournalMaxEntries { get; set; } = 100_000;
    public int MutationJournalMaxReadSize { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the maximum number of mutation executions that may concurrently own SQLite
    /// transaction resources, including indeterminate operations quarantined after a deadline.
    /// </summary>
    public int MaxTrackedMutationExecutions { get; set; } = 8;

    /// <summary>
    /// Gets or sets the bounded lifetime used to drain quarantined mutation work during disposal.
    /// </summary>
    public TimeSpan QuarantinedMutationDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public string HealthRefId { get; set; } = HPDBaseSqliteDefaults.DefaultHealthRefId;
    public string DiagnosticRefId { get; set; } = HPDBaseSqliteDefaults.DefaultDiagnosticRefId;
    public string[] CollectionIds { get; set; } = [];
    public CollectionDefinition[]? Collections { get; set; }
}
