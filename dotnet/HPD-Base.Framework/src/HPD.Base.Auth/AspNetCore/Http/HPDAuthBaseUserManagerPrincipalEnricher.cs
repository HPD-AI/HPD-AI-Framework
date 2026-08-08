using HPD.Auth.Core.Entities;
using HPD.Base.Auth;
using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Auth;

/// <summary>
/// Enriches mapped principals with safe facts from HPD.Auth <see cref="ApplicationUser"/> when Identity services are registered.
/// </summary>
internal sealed class HPDBaseAuthUserManagerPrincipalEnricher(HPDBaseAuthSnapshot options) : IHPDBaseAuthPrincipalEnricher
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
        AddSingleClaim(claims, "hpd.auth.user.is_active", user.IsActive ? "true" : "false");
        AddSingleClaim(claims, "hpd.auth.user.is_deleted", user.IsDeleted ? "true" : "false");
        AddSingleClaim(claims, HPDBaseAuthClaimTypes.SubscriptionTier, user.SubscriptionTier);
        if (!string.IsNullOrWhiteSpace(user.Audience))
            AddSingleClaim(claims, "aud", user.Audience);
        foreach (string requiredAction in user.RequiredActions.Distinct(StringComparer.Ordinal))
            AddMultipleClaim(claims, "hpd.auth.user.required_action", requiredAction);

        if (claims.Count > options.MaxClaims)
            throw new InvalidOperationException("base.auth.actor.projectionFailed");

        var tenantId = principal.CurrentTenantId;
        string userTenantId = BasePrincipalProjectionGuard.Owned(user.InstanceId.ToString(), 256, "tenant");
        if (!string.IsNullOrWhiteSpace(tenantId) && !string.Equals(tenantId, userTenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("base.auth.actor.projectionFailed");
        tenantId ??= userTenantId;

        string? userDisplayName = user.DisplayName ?? user.UserName;
        if (userDisplayName is not null)
            userDisplayName = BasePrincipalProjectionGuard.Owned(userDisplayName, 256, "display name");

        return principal with
        {
            DisplayName = principal.DisplayName ?? userDisplayName,
            CurrentTenantId = tenantId,
            Claims = claims.Count == 0 ? null : claims.ToArray(),
            TenantMemberships = EnsureTenantMembership(principal.TenantMemberships, tenantId)
        };
    }

    private static void AddSingleClaim(List<ClaimValue> claims, string type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        type = BasePrincipalProjectionGuard.Owned(type, 128, "claim type");
        value = BasePrincipalProjectionGuard.Owned(value, 512, "claim value");
        ClaimValue[] existing = claims.Where(claim => string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (existing.Any(claim => !string.Equals(claim.Value, value, StringComparison.Ordinal)))
            throw new InvalidOperationException("base.auth.actor.projectionFailed");
        if (existing.Length != 0)
            return;
        claims.Add(new ClaimValue { Type = type, Value = value });
    }

    private static void AddMultipleClaim(List<ClaimValue> claims, string type, string value)
    {
        type = BasePrincipalProjectionGuard.Owned(type, 128, "claim type");
        value = BasePrincipalProjectionGuard.Owned(value, 128, "required action");
        if (!claims.Any(claim => string.Equals(claim.Type, type, StringComparison.Ordinal) && string.Equals(claim.Value, value, StringComparison.Ordinal)))
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
