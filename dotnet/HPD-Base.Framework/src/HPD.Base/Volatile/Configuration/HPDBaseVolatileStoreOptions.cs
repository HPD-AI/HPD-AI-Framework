
namespace HPD.Base;

/// <summary>
/// Configures the process-local HPD.BASE Volatile record store.
/// </summary>
public sealed class HPDBaseVolatileStoreOptions
{
    internal string StoreId { get; set; } = HPDBaseVolatileDefaults.DefaultStoreId;
    internal string ModuleId { get; set; } = HPDBaseVolatileDefaults.DefaultModuleId;
    internal string ModuleName { get; set; } = HPDBaseVolatileDefaults.DefaultModuleName;
    internal string StoreVersion { get; set; } = HPDBaseVolatileDefaults.DefaultStoreVersion;
    internal string[] CollectionIds { get; set; } = [];
    internal CollectionDefinition[]? Collections { get; set; }
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
    internal bool ContributeModuleDescriptor { get; set; } = true;
    internal bool ContributeCapabilities { get; set; } = true;
    internal bool ContributeHealth { get; set; } = true;
    internal bool ContributeDiagnostics { get; set; } = true;
    internal string HealthRefId { get; set; } = HPDBaseVolatileDefaults.DefaultHealthRefId;
    internal string DiagnosticRefId { get; set; } = HPDBaseVolatileDefaults.DefaultDiagnosticRefId;
}
