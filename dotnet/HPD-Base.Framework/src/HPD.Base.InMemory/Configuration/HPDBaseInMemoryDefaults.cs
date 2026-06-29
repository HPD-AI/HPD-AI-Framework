namespace HPD.Base.InMemory.Configuration;

/// <summary>
/// Default identifiers and version values for the HPD.BASE InMemory store package.
/// </summary>
public static class HPDBaseInMemoryDefaults
{
    /// <summary>The default store id used when no store id is configured.</summary>
    public const string DefaultStoreId = "hpd.base.inmemory.default";
    /// <summary>The default module id used for descriptor contribution.</summary>
    public const string DefaultModuleId = "hpd.base.inmemory";
    /// <summary>The default module display name used for descriptor contribution.</summary>
    public const string DefaultModuleName = "HPD.BASE InMemory Store";
    /// <summary>The default store version advertised by the package.</summary>
    public const string DefaultStoreVersion = "1.0";
    /// <summary>The default health reference id contributed by the package.</summary>
    public const string DefaultHealthRefId = "hpd.base.inmemory.health";
    /// <summary>The default diagnostic reference id contributed by the package.</summary>
    public const string DefaultDiagnosticRefId = "hpd.base.inmemory.diagnostics";
}
