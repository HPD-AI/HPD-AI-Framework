namespace HPD.Base;

/// <summary>
/// Defines stable low-cardinality tag values used by BASE telemetry.
/// </summary>
public static class HPDBaseTelemetryValues
{
    /// <summary>Runtime module id.</summary>
    public const string ModuleRuntime = "hpd.base.runtime";

    /// <summary>InMemory module id.</summary>
    public const string ModuleInMemory = "hpd.base.inmemory";

    /// <summary>SQLite module id.</summary>
    public const string ModuleSqlite = "hpd.base.sqlite";

    /// <summary>Files module id.</summary>
    public const string ModuleFiles = "hpd.base.files";

    /// <summary>Realtime module id.</summary>
    public const string ModuleRealtime = "hpd.base.realtime";

    /// <summary>HPD.Auth adapter module id.</summary>
    public const string ModuleHPDAuth = "hpd.base.auth.hpd_auth";

    /// <summary>InMemory provider kind.</summary>
    public const string ProviderInMemory = "inmemory";

    /// <summary>Files InMemory provider kind.</summary>
    public const string ProviderFilesInMemory = "files.inmemory";

    /// <summary>SQLite provider kind.</summary>
    public const string ProviderSqlite = "sqlite";

    /// <summary>Allow policy effect.</summary>
    public const string PolicyAllow = "allow";

    /// <summary>Deny policy effect.</summary>
    public const string PolicyDeny = "deny";

    /// <summary>Abstain policy effect.</summary>
    public const string PolicyAbstain = "abstain";

    /// <summary>WebSocket transport value.</summary>
    public const string TransportWebSocket = "websocket";

    /// <summary>Admin bypass value.</summary>
    public const string BypassAdmin = "admin";

    /// <summary>Service bypass value.</summary>
    public const string BypassService = "service";
}
