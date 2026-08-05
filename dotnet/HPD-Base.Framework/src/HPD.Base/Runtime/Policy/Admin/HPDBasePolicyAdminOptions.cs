namespace HPD.Base;

/// <summary>
/// Configures admin policy explain behavior.
/// </summary>
public sealed class HPDBasePolicyAdminOptions
{
    /// <summary>
    /// Gets or sets whether service principals may use admin policy explain.
    /// </summary>
    public bool AllowServicePrincipalExplain { get; set; }

    /// <summary>
    /// Gets or sets whether diagnostic references are included by default.
    /// </summary>
    public bool IncludeDiagnosticRefsByDefault { get; set; } = true;
}
