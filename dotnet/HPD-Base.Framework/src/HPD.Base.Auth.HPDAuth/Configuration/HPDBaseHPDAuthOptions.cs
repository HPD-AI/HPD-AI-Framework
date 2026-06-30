using HPD.Base.Policy;
using HPD.Base.Auth.HPDAuth.Policy;

namespace HPD.Base.Auth.HPDAuth.Configuration;

/// <summary>
/// Configures the HPD.Auth adapter for HPD.BASE runtime identity and policy mapping.
/// </summary>
public sealed class HPDBaseHPDAuthOptions
{
    /// <summary>
    /// Gets or sets whether authenticated callers are required when no collection rule or grant allows anonymous access.
    /// </summary>
    public bool RequireAuthenticatedByDefault { get; set; } = true;

    /// <summary>
    /// Gets or sets whether admin callers may bypass collection rules and grants.
    /// </summary>
    public bool AllowAdminBypass { get; set; } = true;

    /// <summary>
    /// Gets or sets whether service principals may bypass collection rules and grants.
    /// </summary>
    public bool AllowServiceBypass { get; set; }

    /// <summary>
    /// Gets or sets whether adapter diagnostics should report missing HPD.Auth host services.
    /// </summary>
    public bool RequireHPDAuthServices { get; set; } = true;

    /// <summary>
    /// Gets or sets how the adapter composes with an optional inner policy evaluator.
    /// </summary>
    public HPDAuthBasePolicyCompositionMode PolicyCompositionMode { get; set; } = HPDAuthBasePolicyCompositionMode.HPDAuthOnly;

    /// <summary>
    /// Gets or sets the role names that classify a caller as a BASE admin.
    /// </summary>
    public string[] AdminRoleNames { get; set; } = ["Admin"];

    /// <summary>
    /// Gets or sets the claim type used for HPD.Auth tenant identity.
    /// </summary>
    public string TenantClaimType { get; set; } = HPDAuthBaseClaimTypes.InstanceId;

    /// <summary>
    /// Gets or sets the claim type used for HPD.Auth subscription tier.
    /// </summary>
    public string SubscriptionTierClaimType { get; set; } = HPDAuthBaseClaimTypes.SubscriptionTier;

    /// <summary>
    /// Gets or sets the claim type used for HPD.Auth session identity.
    /// </summary>
    public string SessionIdClaimType { get; set; } = "sid";

    /// <summary>
    /// Gets or sets the safe claim type used for credential identity, or <c>null</c> to disable credential mapping.
    /// </summary>
    public string? CredentialIdClaimType { get; set; }

    /// <summary>
    /// Gets or sets the claim types inspected for stable user identity.
    /// </summary>
    public string[] SubjectIdClaimTypes { get; set; } =
    [
        "sub",
        "nameidentifier",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    ];

    /// <summary>
    /// Gets or sets the claim types inspected for display names.
    /// </summary>
    public string[] DisplayNameClaimTypes { get; set; } =
    [
        "name",
        "preferred_username",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"
    ];

    /// <summary>
    /// Gets or sets the claim types inspected for role membership.
    /// </summary>
    public string[] RoleClaimTypes { get; set; } =
    [
        "role",
        "roles",
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    ];

    /// <summary>
    /// Gets or sets the claim types inspected for service principal identity.
    /// </summary>
    public string[] ServicePrincipalClaimTypes { get; set; } =
    [
        "client_id",
        "azp",
        "aud",
        "appid"
    ];

    /// <summary>
    /// Gets or sets fragments that cause claim values to be excluded from copied BASE principal claims.
    /// </summary>
    public string[] SensitiveClaimTypeFragments { get; set; } =
    [
        "token",
        "secret",
        "password",
        "credential",
        "authorization",
        "securitystamp",
        "refresh"
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
    /// Gets or sets static collection rules evaluated by the adapter.
    /// </summary>
    public HPDAuthBaseCollectionRule[] CollectionRules { get; set; } = [];

    /// <summary>
    /// Gets or sets static BASE grants evaluated by the adapter.
    /// </summary>
    public AccessGrant[] StaticGrants { get; set; } = [];
}

/// <summary>
/// Names HPD.Auth claim types understood by the BASE adapter.
/// </summary>
public static class HPDAuthBaseClaimTypes
{
    /// <summary>
    /// HPD.Auth tenant instance id claim.
    /// </summary>
    public const string InstanceId = "instance_id";

    /// <summary>
    /// HPD.Auth subscription tier claim.
    /// </summary>
    public const string SubscriptionTier = "subscription_tier";
}
