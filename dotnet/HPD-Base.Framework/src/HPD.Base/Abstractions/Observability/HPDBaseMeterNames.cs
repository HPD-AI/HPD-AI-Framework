namespace HPD.Base;

/// <summary>
/// Defines stable <see cref="System.Diagnostics.Metrics.Meter" /> names emitted by HPD.BASE packages.
/// </summary>
public static class HPDBaseMeterNames
{
    /// <summary>Runtime/core orchestration meter name.</summary>
    public const string Runtime = "HPD.Base";

    /// <summary>ASP.NET Core projection meter name.</summary>
    public const string AspNetCore = "HPD.Base";

    /// <summary>Volatile provider meter name.</summary>
    public const string Volatile = "HPD.Base";

    /// <summary>SQLite provider meter name.</summary>
    public const string Sqlite = "HPD.Base";

    /// <summary>Files runtime meter name.</summary>
    public const string Files = "HPD.Base";

    /// <summary>Files ASP.NET Core projection meter name.</summary>
    public const string FilesAspNetCore = "HPD.Base";

    /// <summary>Files Volatile provider meter name.</summary>
    public const string FilesVolatile = "HPD.Base";

    /// <summary>Realtime runtime meter name.</summary>
    public const string Realtime = "HPD.Base";

    /// <summary>Realtime ASP.NET Core projection meter name.</summary>
    public const string RealtimeAspNetCore = "HPD.Base";

    /// <summary>HPD.Auth adapter meter name.</summary>
    public const string HPDAuth = "HPD.Base";

    /// <summary>HPD.Auth ASP.NET Core bridge meter name.</summary>
    public const string HPDAuthAspNetCore = "HPD.Base";

    /// <summary>Core meter names used by the BASE runtime spine.</summary>
    public static readonly string[] Core = [Runtime];

    /// <summary>Record store provider meter names.</summary>
    public static readonly string[] Stores = [Volatile, Sqlite];

    /// <summary>Optional module meter names.</summary>
    public static readonly string[] OptionalModules = [Files, FilesAspNetCore, FilesVolatile, Realtime, RealtimeAspNetCore, HPDAuth, HPDAuthAspNetCore];

    /// <summary>All known BASE meter names.</summary>
    public static readonly string[] All = [Runtime, AspNetCore, Volatile, Sqlite, Files, FilesAspNetCore, FilesVolatile, Realtime, RealtimeAspNetCore, HPDAuth, HPDAuthAspNetCore];
}
