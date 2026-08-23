using HPD.Base;

namespace HPD.Base.Sqlite;
/// <summary>Configures the durable HPD.BASE SQLite record store.</summary>
public sealed class HPDBaseSqliteOptions
{
    /// <summary>Gets or sets store Id.</summary>
    public string StoreId { get; set; } = HPDBaseSqliteDefaults.DefaultStoreId;
    /// <summary>Gets or sets module Id.</summary>
    public string ModuleId { get; set; } = HPDBaseSqliteDefaults.DefaultModuleId;
    /// <summary>Gets or sets module Name.</summary>
    public string ModuleName { get; set; } = HPDBaseSqliteDefaults.DefaultModuleName;
    /// <summary>Gets or sets store Version.</summary>
    public string StoreVersion { get; set; } = HPDBaseSqliteDefaults.DefaultStoreVersion;
    /// <summary>
    /// Gets or sets the exact SQLite connection string to use.
    /// </summary>
    /// <remarks>
    /// This value wins over <see cref = "DataSource"/>. Use it when another host or integration package
    /// has already resolved the full connection string, such as an Aspire connection string resource.
    /// </remarks>
    public string? ConnectionString { get; set; }
    /// <summary>
    /// Gets or sets the SQLite data source used to build a connection string when <see cref = "ConnectionString"/> is not set.
    /// </summary>
    /// <remarks>
    /// This is the SQLite <c>Data Source</c> value, such as a database file path or <c>:memory:</c>.
    /// It is not a directory path.
    /// </remarks>
    public string? DataSource { get; set; }
    /// <summary>Gets or sets schema Prefix.</summary>
    public string SchemaPrefix { get; set; } = HPDBaseSqliteDefaults.DefaultSchemaPrefix;
    /// <summary>Gets or sets enable Wal.</summary>
    public bool EnableWal { get; set; } = true;
    /// <summary>Gets or sets busy Timeout.</summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets command Timeout.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets default Page Size.</summary>
    public int DefaultPageSize { get; set; } = 100;
    /// <summary>Gets or sets max Page Size.</summary>
    public int MaxPageSize { get; set; } = 1_000;
    /// <summary>Gets or sets max Filter Depth.</summary>
    public int MaxFilterDepth { get; set; } = 8;
    /// <summary>Gets or sets max Filter Nodes.</summary>
    public int MaxFilterNodes { get; set; } = 128;
    /// <summary>Gets or sets max In Values.</summary>
    public int MaxInValues { get; set; } = 100;
    /// <summary>Gets or sets max Sort Fields.</summary>
    public int MaxSortFields { get; set; } = 8;
    /// <summary>Gets or sets max Select Fields.</summary>
    public int MaxSelectFields { get; set; } = 64;
    /// <summary>Gets or sets allow Client Requested Ids.</summary>
    public bool AllowClientRequestedIds { get; set; } = true;
    /// <summary>Gets or sets contribute Module Descriptor.</summary>
    public bool ContributeModuleDescriptor { get; set; } = true;
    /// <summary>Gets or sets contribute Capabilities.</summary>
    public bool ContributeCapabilities { get; set; } = true;
    /// <summary>Gets or sets contribute Health.</summary>
    public bool ContributeHealth { get; set; } = true;
    /// <summary>Gets or sets contribute Diagnostics.</summary>
    public bool ContributeDiagnostics { get; set; } = true;
    /// <summary>Gets or sets contribute Relational Descriptors.</summary>
    public bool ContributeRelationalDescriptors { get; set; } = true;
    /// <summary>Gets or sets initialize SQLite PCLRaw.</summary>
    public bool InitializeSQLitePCLRaw { get; set; } = true;
    /// <summary>Gets or sets mutation Journal Retention.</summary>
    public TimeSpan MutationJournalRetention { get; set; } = TimeSpan.FromDays(7);
    /// <summary>Gets or sets mutation Journal Max Entries.</summary>
    public int MutationJournalMaxEntries { get; set; } = 100_000;
    /// <summary>Gets or sets mutation Journal Max Read Size.</summary>
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
    /// <summary>Gets or sets whether host-only backup and restore administration is enabled.</summary>
    public bool AdministrationEnabled { get; set; }
    /// <summary>Gets or sets the maximum complete backup artifact size.</summary>
    public long MaxBackupArtifactBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    /// <summary>Gets or sets the administration lease acquisition timeout.</summary>
    public TimeSpan AdministrationAcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the maximum wait for synchronous native backup completion.</summary>
    public TimeSpan NativeBackupCompletionWait { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets or sets the maximum restore staging and installation lifetime.</summary>
    public TimeSpan RestoreStagingTimeout { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Gets or sets the maximum integrity-check lifetime.</summary>
    public TimeSpan IntegrityCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets or sets the maximum number of quarantined administration executions.</summary>
    public int MaxQuarantinedAdministrationExecutions { get; set; } = 1;
    /// <summary>Gets or sets the maximum retained runnable activation rows.</summary>
    public int MaxPendingActivationRows { get; set; } = 1_000_000;
    /// <summary>Gets or sets the maximum retained active activation rows.</summary>
    public int MaxClaimedActivationRows { get; set; } = 1_000_000;
    /// <summary>Gets or sets the maximum retained terminal activation rows.</summary>
    public int MaxTerminalActivationRows { get; set; } = 1_000_000;
    /// <summary>Gets or sets health Ref Id.</summary>
    public string HealthRefId { get; set; } = HPDBaseSqliteDefaults.DefaultHealthRefId;
    /// <summary>Gets or sets diagnostic Ref Id.</summary>
    public string DiagnosticRefId { get; set; } = HPDBaseSqliteDefaults.DefaultDiagnosticRefId;
    /// <summary>Gets or sets the complete closed collection schemas installed in this store.</summary>
    public CollectionDefinition[] Collections { get; set; } = [];
    internal BaseExportedSubjectDefinition[] ExportedSubjects { get; set; } = [];
    internal BaseRegisteredModuleMutationDefinition[] ModuleMutations { get; set; } = [];
    internal BaseModuleGenerationCellDefinition[] ModuleGenerationCells { get; set; } = [];
    internal BaseSemanticActivationKeyDefinition[] SemanticActivations { get; set; } = [];
    internal string SemanticActivationApplicationId { get; set; } = string.Empty;
    internal long SemanticActivationOwnerGeneration { get; set; }
    internal BaseSemanticActivationMigrationDefinition[] SemanticActivationMigrations { get; set; } = [];
    internal byte[] SemanticActivationDefinitionSetChecksum { get; set; } = [];
    internal BaseSubjectLifecycleConsumerDefinition[] SubjectLifecycleConsumers { get; set; } = [];
    internal BaseSubjectRetirementConsumerDefinition[] SubjectRetirementConsumers { get; set; } = [];
    internal BaseSubjectRetirementPolicy[] SubjectRetirementPolicies { get; set; } = [];
    internal BaseSubjectLifecycleInspectionAuthority[] SubjectLifecycleInspectionAuthorities { get; set; } = [];
}
