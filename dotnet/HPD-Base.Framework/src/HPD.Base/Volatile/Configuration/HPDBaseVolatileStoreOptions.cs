
namespace HPD.Base;

/// <summary>
/// Configures the process-local HPD.BASE Volatile record store.
/// </summary>
public sealed class HPDBaseVolatileStoreOptions
{
    /// <summary>Gets or sets the store id advertised by the store and used for registry binding.</summary>
    internal string StoreId { get; set; } = HPDBaseVolatileDefaults.DefaultStoreId;
    /// <summary>Gets or sets the module id used for descriptor contribution.</summary>
    internal string ModuleId { get; set; } = HPDBaseVolatileDefaults.DefaultModuleId;
    /// <summary>Gets or sets the module display name used for descriptor contribution.</summary>
    internal string ModuleName { get; set; } = HPDBaseVolatileDefaults.DefaultModuleName;
    /// <summary>Gets or sets the store version advertised by the store.</summary>
    internal string StoreVersion { get; set; } = HPDBaseVolatileDefaults.DefaultStoreVersion;
    /// <summary>Gets or sets collection ids explicitly bound to this store registration.</summary>
    internal string[] CollectionIds { get; set; } = [];
    /// <summary>Gets or sets optional demo or test collection definitions contributed by the package.</summary>
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
    /// <summary>Gets or sets whether the package contributes a store module descriptor.</summary>
    internal bool ContributeModuleDescriptor { get; set; } = true;
    /// <summary>Gets or sets whether the package contributes capability descriptors.</summary>
    internal bool ContributeCapabilities { get; set; } = true;
    /// <summary>Gets or sets whether the package contributes health metadata.</summary>
    internal bool ContributeHealth { get; set; } = true;
    /// <summary>Gets or sets whether the package contributes diagnostic metadata.</summary>
    internal bool ContributeDiagnostics { get; set; } = true;
    /// <summary>Gets or sets the health reference id used by descriptor and health contribution.</summary>
    internal string HealthRefId { get; set; } = HPDBaseVolatileDefaults.DefaultHealthRefId;
    /// <summary>Gets or sets the diagnostic reference id used by descriptor and diagnostic contribution.</summary>
    internal string DiagnosticRefId { get; set; } = HPDBaseVolatileDefaults.DefaultDiagnosticRefId;
}
