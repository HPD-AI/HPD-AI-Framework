namespace HPD.Base.Sqlite;

/// <summary>Default values for the HPD.BASE SQLite record store.</summary>
public static class HPDBaseSqliteDefaults
{
    /// <summary>Gets the default store identifier.</summary>
    public const string DefaultStoreId = "sqlite";
    /// <summary>Gets the default module identifier.</summary>
    public const string DefaultModuleId = "hpd.base.sqlite";
    /// <summary>Gets the default module display name.</summary>
    public const string DefaultModuleName = "HPD.BASE SQLite";
    /// <summary>Gets the default store implementation version.</summary>
    public const string DefaultStoreVersion = "0.2.0";
    /// <summary>Gets the default SQLite schema prefix.</summary>
    public const string DefaultSchemaPrefix = "hpd_base_";
    /// <summary>Gets the default health descriptor reference.</summary>
    public const string DefaultHealthRefId = "hpd.base.sqlite.health";
    /// <summary>Gets the default diagnostic descriptor reference.</summary>
    public const string DefaultDiagnosticRefId = "hpd.base.sqlite.diagnostics";
    /// <summary>Gets the maximum operations accepted by one atomic execution.</summary>
    public const int MaximumBatchOperations = 100;
    /// <summary>Gets the maximum canonical payload bytes accepted by one atomic execution.</summary>
    public const long MaximumBatchCanonicalPayloadBytes = 1_048_576;
}
