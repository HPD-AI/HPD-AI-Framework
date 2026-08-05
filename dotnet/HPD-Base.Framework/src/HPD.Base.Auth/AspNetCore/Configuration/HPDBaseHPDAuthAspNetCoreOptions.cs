namespace HPD.Base.Auth;

/// <summary>
/// Configures ASP.NET Core integration for the HPD.Auth BASE adapter.
/// </summary>
public sealed class HPDBaseHPDAuthAspNetCoreOptions
{
    /// <summary>
    /// Gets or sets whether the ASP.NET mapper should use HPD.Auth <c>ITenantContext</c> as a tenant fallback.
    /// </summary>
    public bool UseTenantContextFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the default role used by the BASE admin policy bridge.
    /// </summary>
    public string AdminRoleName { get; set; } = "Admin";
}
