namespace HPD.Base;

/// <summary>
/// Defines stable <see cref="System.Diagnostics.ActivitySource" /> names emitted by HPD.BASE packages.
/// </summary>
public static class HPDBaseActivitySourceNames
{
    /// <summary>Runtime/core orchestration activity source name.</summary>
    public const string Runtime = "HPD.Base";

    /// <summary>ASP.NET Core projection activity source name.</summary>
    public const string AspNetCore = "HPD.Base";

    /// <summary>Volatile provider activity source name.</summary>
    public const string Volatile = "HPD.Base";

    /// <summary>SQLite provider activity source name.</summary>
    public const string Sqlite = "HPD.Base";

    /// <summary>Files runtime activity source name.</summary>
    public const string Files = "HPD.Base";

    /// <summary>Files ASP.NET Core projection activity source name.</summary>
    public const string FilesAspNetCore = "HPD.Base";

    /// <summary>Files Volatile provider activity source name.</summary>
    public const string FilesVolatile = "HPD.Base";

    /// <summary>Realtime runtime activity source name.</summary>
    public const string Realtime = "HPD.Base";

    /// <summary>Realtime ASP.NET Core projection activity source name.</summary>
    public const string RealtimeAspNetCore = "HPD.Base";

    /// <summary>HPD.Auth adapter activity source name.</summary>
    public const string HPDAuth = "HPD.Base";

    /// <summary>HPD.Auth ASP.NET Core bridge activity source name.</summary>
    public const string HPDAuthAspNetCore = "HPD.Base";

    /// <summary>Core source names used by the BASE runtime spine.</summary>
    public static readonly string[] Core = [Runtime];

    /// <summary>Record store provider source names.</summary>
    public static readonly string[] Stores = [Volatile, Sqlite];

    /// <summary>Optional module source names.</summary>
    public static readonly string[] OptionalModules = [Files, FilesAspNetCore, FilesVolatile, Realtime, RealtimeAspNetCore, HPDAuth, HPDAuthAspNetCore];

    /// <summary>All known BASE source names.</summary>
    public static readonly string[] All = [Runtime, AspNetCore, Volatile, Sqlite, Files, FilesAspNetCore, FilesVolatile, Realtime, RealtimeAspNetCore, HPDAuth, HPDAuthAspNetCore];
}
