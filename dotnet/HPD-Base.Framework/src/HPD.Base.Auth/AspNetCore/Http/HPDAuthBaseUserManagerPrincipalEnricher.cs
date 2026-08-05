using HPD.Auth.Core.Entities;
using HPD.Base.Auth;
using HPD.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Auth;

/// <summary>
/// Enriches mapped principals with safe facts from HPD.Auth <see cref="ApplicationUser"/> when Identity services are registered.
/// </summary>
public sealed class HPDAuthBaseUserManagerPrincipalEnricher : IHPDAuthBaseHttpPrincipalEnricher
{
    /// <inheritdoc />
    public async ValueTask<PrincipalContext> EnrichAsync(
        HttpContext httpContext,
        PrincipalContext principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(principal.SubjectId))
            return principal;

        var userManager = httpContext.RequestServices.GetService<UserManager<ApplicationUser>>();
        if (userManager is null)
            return principal;

        var user = await userManager.FindByIdAsync(principal.SubjectId).ConfigureAwait(false);
        if (user is null)
            return principal;

        var claims = new List<ClaimValue>(principal.Claims ?? []);
        AddClaim(claims, "hpd.auth.user.is_active", user.IsActive ? "true" : "false");
        AddClaim(claims, "hpd.auth.user.is_deleted", user.IsDeleted ? "true" : "false");
        AddClaim(claims, HPDAuthBaseClaimTypes.SubscriptionTier, user.SubscriptionTier);
        if (!string.IsNullOrWhiteSpace(user.Audience))
            AddClaim(claims, "aud", user.Audience);
        if (user.RequiredActions.Count > 0)
            AddClaim(claims, "hpd.auth.user.required_actions", string.Join(",", user.RequiredActions));

        var tenantId = principal.CurrentTenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            tenantId = user.InstanceId.ToString();

        return principal with
        {
            DisplayName = principal.DisplayName ?? user.DisplayName ?? user.UserName,
            CurrentTenantId = tenantId,
            Claims = claims.Count == 0 ? null : claims.ToArray(),
            TenantMemberships = EnsureTenantMembership(principal.TenantMemberships, tenantId)
        };
    }

    private static void AddClaim(List<ClaimValue> claims, string type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (claims.Any(claim => string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase)))
            return;

        claims.Add(new ClaimValue { Type = type, Value = value });
    }

    private static TenantMembership[]? EnsureTenantMembership(TenantMembership[]? memberships, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return memberships;
        if (memberships?.Any(membership => string.Equals(membership.TenantId, tenantId, StringComparison.Ordinal)) == true)
            return memberships;

        return (memberships ?? []).Concat([new TenantMembership { TenantId = tenantId, Source = "hpd.auth" }]).ToArray();
    }
}
