namespace HPD.Base;

/// <summary>
/// Default identifiers and version values for the HPD.BASE Volatile store package.
/// </summary>
internal static class HPDBaseVolatileDefaults
{
    /// <summary>The default store id used when no store id is configured.</summary>
    public const string DefaultStoreId = "hpd.base.volatile.default";
    /// <summary>The default module id used for descriptor contribution.</summary>
    public const string DefaultModuleId = "hpd.base.volatile";
    /// <summary>The default module display name used for descriptor contribution.</summary>
    public const string DefaultModuleName = "HPD.BASE Volatile Store";
    /// <summary>The default store version advertised by the package.</summary>
    public const string DefaultStoreVersion = "1.0";
    /// <summary>The default health reference id contributed by the package.</summary>
    public const string DefaultHealthRefId = "hpd.base.volatile.health";
    /// <summary>The default diagnostic reference id contributed by the package.</summary>
    public const string DefaultDiagnosticRefId = "hpd.base.volatile.diagnostics";
    /// <summary>The maximum operation count advertised for one ordered batch.</summary>
    public const int MaximumBatchOperations = 100;
    /// <summary>The maximum canonical payload size advertised for one ordered batch.</summary>
    public const long MaximumBatchCanonicalPayloadBytes = 1_048_576;
}
