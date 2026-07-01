namespace HPD.Base.Observability;

/// <summary>
/// Defines stable <see cref="System.Diagnostics.ActivitySource" /> names emitted by HPD.BASE packages.
/// </summary>
public static class HPDBaseActivitySourceNames
{
    /// <summary>Runtime/core orchestration activity source name.</summary>
    public const string Runtime = "HPD.Base.Runtime";

    /// <summary>ASP.NET Core projection activity source name.</summary>
    public const string AspNetCore = "HPD.Base.AspNetCore";

    /// <summary>InMemory provider activity source name.</summary>
    public const string InMemory = "HPD.Base.InMemory";

    /// <summary>SQLite provider activity source name.</summary>
    public const string Sqlite = "HPD.Base.Sqlite";

    /// <summary>Files runtime activity source name.</summary>
    public const string Files = "HPD.Base.Files";

    /// <summary>Files ASP.NET Core projection activity source name.</summary>
    public const string FilesAspNetCore = "HPD.Base.Files.AspNetCore";

    /// <summary>Files InMemory provider activity source name.</summary>
    public const string FilesInMemory = "HPD.Base.Files.InMemory";

    /// <summary>Realtime runtime activity source name.</summary>
    public const string Realtime = "HPD.Base.Realtime";

    /// <summary>Realtime ASP.NET Core projection activity source name.</summary>
    public const string RealtimeAspNetCore = "HPD.Base.Realtime.AspNetCore";

    /// <summary>HPD.Auth adapter activity source name.</summary>
    public const string HPDAuth = "HPD.Base.Auth.HPDAuth";

    /// <summary>HPD.Auth ASP.NET Core bridge activity source name.</summary>
    public const string HPDAuthAspNetCore = "HPD.Base.Auth.HPDAuth.AspNetCore";

    /// <summary>Core source names used by the BASE runtime spine.</summary>
    public static readonly string[] Core = [Runtime];

    /// <summary>Record store provider source names.</summary>
    public static readonly string[] Stores = [InMemory, Sqlite];

    /// <summary>Optional module source names.</summary>
    public static readonly string[] OptionalModules = [Files, FilesAspNetCore, FilesInMemory, Realtime, RealtimeAspNetCore, HPDAuth, HPDAuthAspNetCore];

    /// <summary>All known BASE source names.</summary>
    public static readonly string[] All = [Runtime, AspNetCore, InMemory, Sqlite, Files, FilesAspNetCore, FilesInMemory, Realtime, RealtimeAspNetCore, HPDAuth, HPDAuthAspNetCore];
}
