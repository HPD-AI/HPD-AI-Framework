namespace HPD.Base.AspNetCore;

/// <summary>
/// Configures HTTP ProblemDetails responses emitted by HPD.BASE endpoints.
/// </summary>
public sealed class HPDBaseProblemDetailsOptions
{
    /// <summary>
    /// Gets or sets whether public-safe operation diagnostics may be included in ProblemDetails extensions.
    /// </summary>
    public bool IncludeSafeDiagnostics { get; set; } = true;

    /// <summary>
    /// Gets or sets whether warnings should be included in ProblemDetails extensions.
    /// </summary>
    public bool IncludeWarnings { get; set; } = true;
}
