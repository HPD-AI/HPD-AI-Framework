using HPD.Base.AspNetCore.EndpointMapping;

namespace HPD.Base.AspNetCore.Configuration;

/// <summary>
/// Configures the ASP.NET Core projection for HPD.BASE.
/// </summary>
public sealed class HPDBaseAspNetCoreOptions
{
    /// <summary>
    /// Gets or sets endpoint mapping options.
    /// </summary>
    public HPDBaseEndpointOptions Endpoints { get; set; } = new();

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
}
