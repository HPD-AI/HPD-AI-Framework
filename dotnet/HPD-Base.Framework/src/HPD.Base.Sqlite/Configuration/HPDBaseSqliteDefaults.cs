namespace HPD.Base.Sqlite.Configuration;

/// <summary>Default values for the HPD.BASE SQLite record store.</summary>
public static class HPDBaseSqliteDefaults
{
    public const string DefaultStoreId = "sqlite";
    public const string DefaultModuleId = "hpd.base.sqlite";
    public const string DefaultModuleName = "HPD.BASE SQLite";
    public const string DefaultStoreVersion = "0.2.0";
    public const string DefaultSchemaPrefix = "hpd_base_";
    public const string DefaultHealthRefId = "hpd.base.sqlite.health";
    public const string DefaultDiagnosticRefId = "hpd.base.sqlite.diagnostics";
}
