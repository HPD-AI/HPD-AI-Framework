using HPD.Base.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.DependencyInjection;

/// <summary>
/// Provides authorization policy helpers for the HPD.Auth BASE adapter.
/// </summary>
public static class HPDBaseHPDAuthAuthorizationOptionsExtensions
{
    /// <summary>
    /// Adds a BASE admin policy that uses an HPD.Auth admin role.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    /// <param name="basePolicyName">The BASE admin policy name.</param>
    /// <param name="adminRoleName">The HPD.Auth admin role name.</param>
    /// <returns>The same authorization options for chaining.</returns>
    public static AuthorizationOptions AddHPDBaseHPDAuthAdminPolicy(
        this AuthorizationOptions options,
        string basePolicyName = HPDBasePolicies.Admin,
        string adminRoleName = "Admin")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePolicyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminRoleName);

        options.AddPolicy(basePolicyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireRole(adminRoleName);
        });

        return options;
    }
}
