using HPD.Base.AspNetCore;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Configures the ASP.NET Core projection for HPD.BASE.
/// </summary>
public sealed class HPDBaseAspNetCoreOptions
{
    /// <summary>
    /// Gets or sets ProblemDetails mapping options.
    /// </summary>
    public HPDBaseProblemDetailsOptions ProblemDetails { get; set; } = new();

    /// <summary>
    /// Gets or sets HTTP principal mapping options.
    /// </summary>
    public HPDBaseHttpAuthOptions Auth { get; set; } = new();

    /// <summary>
    /// Gets or sets request context projection options.
    /// </summary>
    public HPDBaseHttpRequestContextOptions RequestContext { get; set; } = new();

    /// <summary>
    /// Gets or sets transport limit options.
    /// </summary>
    public HPDBaseHttpLimitOptions Limits { get; set; } = new();

    /// <summary>Gets or sets bounded administration artifact staging.</summary>
    public HPDBaseAdministrationHttpOptions Administration { get; set; } = new();
}

/// <summary>Configures private staging for confirmed backup, validation, and restore transport.</summary>
public sealed class HPDBaseAdministrationHttpOptions
{
    /// <summary>Gets or sets the absolute private staging directory; required when artifact administration endpoints are mapped.</summary>
    public string? StagingRoot { get; set; }
    /// <summary>Gets or sets the maximum staged artifact bytes.</summary>
    public long MaxArtifactBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    /// <summary>Gets or sets the maximum concurrent staging slots.</summary>
    public int MaxConcurrentStaging { get; set; } = 2;
    /// <summary>Gets or sets the strict close and deletion deadline.</summary>
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
