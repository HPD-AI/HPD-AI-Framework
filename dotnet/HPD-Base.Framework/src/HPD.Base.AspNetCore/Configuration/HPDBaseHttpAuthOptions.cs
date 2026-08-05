namespace HPD.Base.AspNetCore;

/// <summary>
/// Configures conservative mapping from ASP.NET Core principals to HPD.BASE principals.
/// </summary>
public sealed class HPDBaseHttpAuthOptions
{
    /// <summary>
    /// Gets the claim types inspected for a stable subject id, in priority order.
    /// </summary>
    public string[] SubjectIdClaimTypes { get; set; } =
    [
        "sub",
        "nameidentifier",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    ];

    /// <summary>
    /// Gets the claim types inspected for display names.
    /// </summary>
    public string[] DisplayNameClaimTypes { get; set; } =
    [
        "name",
        "preferred_username",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"
    ];

    /// <summary>
    /// Gets the claim types inspected for role membership.
    /// </summary>
    public string[] RoleClaimTypes { get; set; } =
    [
        "role",
        "roles",
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    ];

    /// <summary>
    /// Gets or sets the claim type used for the current tenant id.
    /// </summary>
    public string? TenantIdClaimType { get; set; } = "tenant_id";

    /// <summary>
    /// Gets or sets the claim type used for tenant membership ids.
    /// </summary>
    public string? TenantMembershipClaimType { get; set; } = "tenant_ids";

    /// <summary>
    /// Gets or sets the claim type used for the current session id.
    /// </summary>
    public string? SessionIdClaimType { get; set; } = "sid";

    /// <summary>
    /// Gets role values that classify a principal as an admin.
    /// </summary>
    public string[] AdminRoleNames { get; set; } = [];

    /// <summary>
    /// Gets claim types whose presence classifies a principal as a service principal.
    /// </summary>
    public string[] ServicePrincipalClaimTypes { get; set; } =
    [
        "azp",
        "client_id"
    ];

    /// <summary>
    /// Gets or sets the maximum number of claims copied into the BASE principal.
    /// </summary>
    public int MaxClaims { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum number of roles copied into the BASE principal.
    /// </summary>
    public int MaxRoles { get; set; } = 32;

    /// <summary>
    /// Gets claim type fragments that are excluded from copied claim values.
    /// </summary>
    public string[] SensitiveClaimTypeFragments { get; set; } =
    [
        "token",
        "secret",
        "password",
        "credential",
        "authorization"
    ];
}
