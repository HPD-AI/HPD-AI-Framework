
namespace HPD.Base;

/// <summary>
/// Configures the process-local HPD.BASE InMemory record store.
/// </summary>
public sealed class HPDBaseInMemoryStoreOptions
{
    internal string StoreId { get; set; } = HPDBaseInMemoryDefaults.DefaultStoreId;
    internal string ModuleId { get; set; } = HPDBaseInMemoryDefaults.DefaultModuleId;
    internal string ModuleName { get; set; } = HPDBaseInMemoryDefaults.DefaultModuleName;
    internal string StoreVersion { get; set; } = HPDBaseInMemoryDefaults.DefaultStoreVersion;
    internal string[] CollectionIds { get; set; } = [];
    internal CollectionDefinition[]? Collections { get; set; }
    internal BaseExportedSubjectDefinition[] ExportedSubjects { get; set; } = [];
    /// <summary>Gets or sets the default page size used when a query omits page size.</summary>
    public int DefaultPageSize { get; set; } = 100;
    /// <summary>Gets or sets the maximum page size advertised and accepted by the store.</summary>
    public int MaxPageSize { get; set; } = 1_000;
    /// <summary>Gets or sets the maximum supported filter depth.</summary>
    public int MaxFilterDepth { get; set; } = 8;
    /// <summary>Gets or sets the maximum supported filter node count.</summary>
    public int MaxFilterNodes { get; set; } = 128;
    /// <summary>Gets or sets the maximum serialized query length supported by the store.</summary>
    public int MaxSerializedQueryLength { get; set; } = 16_384;
    /// <summary>Gets or sets the maximum number of values accepted by <c>In</c> filters.</summary>
    public int MaxInValues { get; set; } = 100;
    /// <summary>Gets or sets the maximum number of sort fields accepted by the store.</summary>
    public int MaxSortFields { get; set; } = 8;
    /// <summary>Gets or sets the maximum number of selected payload fields accepted by the store.</summary>
    public int MaxSelectFields { get; set; } = 64;
    /// <summary>Gets or sets an optional maximum item count for streaming enumeration.</summary>
    public int? MaxStreamItems { get; set; }
    /// <summary>Gets or sets whether callers may request record ids on create.</summary>
    public bool AllowClientRequestedIds { get; set; } = true;
    /// <summary>Gets or sets whether the store advertises streaming support.</summary>
    public bool EnableStreamingCapability { get; set; } = true;
    /// <summary>Gets or sets the maximum number of indexed vector carriers.</summary>
    public int MaxVectorIndexedRecords { get; set; } = 10_000;
    /// <summary>Gets or sets the maximum owned vector bytes in the currently published root.</summary>
    public long MaxVectorBytes { get; set; } = 67_108_864;
    /// <summary>Gets or sets the maximum authoritative records in one vector-bearing collection.</summary>
    public int MaxVectorSourceRecordsPerCollection { get; set; } = 100_000;
    internal bool ContributeModuleDescriptor { get; set; } = true;
    internal bool ContributeCapabilities { get; set; } = true;
    internal bool ContributeHealth { get; set; } = true;
    internal bool ContributeDiagnostics { get; set; } = true;
    internal string HealthRefId { get; set; } = HPDBaseInMemoryDefaults.DefaultHealthRefId;
    internal string DiagnosticRefId { get; set; } = HPDBaseInMemoryDefaults.DefaultDiagnosticRefId;
}
