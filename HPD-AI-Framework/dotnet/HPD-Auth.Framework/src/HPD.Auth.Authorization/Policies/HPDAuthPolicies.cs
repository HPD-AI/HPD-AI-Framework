using System.Security.Claims;
using HPD.Auth.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Policies;

/// <summary>
/// Central registry of all built-in HPD.Auth authorization policy names and the
/// <see cref="RegisterPolicies"/> method that configures them on an
/// <see cref="AuthorizationOptions"/> instance.
/// </summary>
/// <remarks>
/// Policy name constants are used both here (during registration) and in consuming
/// code (e.g. <c>[Authorize(Policy = HPDAuthPolicies.RequireAdmin)]</c>) so that
/// a single rename here covers all usages without string duplication.
/// </remarks>
public static class HPDAuthPolicies
{
    // ─────────────────────────────────────────────────────────────────────────
    // Policy name constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Requires the user to be in the <c>Admin</c> role.</summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>Requires the user to be in the <c>Admin</c> or <c>Moderator</c> role.</summary>
    public const string RequireAdminOrModerator = "RequireAdminOrModerator";

    /// <summary>Requires a <c>pro</c> or <c>enterprise</c> subscription tier.</summary>
    public const string RequirePremium = "RequirePremium";

    /// <summary>Requires an <c>enterprise</c> subscription tier.</summary>
    public const string RequireEnterprise = "RequireEnterprise";

    /// <summary>Requires a verified email address claim.</summary>
    public const string RequireEmailVerified = "RequireEmailVerified";

    /// <summary>Requires the user to be authenticated with at least a free subscription.</summary>
    public const string CanInstallApps = "CanInstallApps";

    /// <summary>Requires a <c>pro</c> or higher subscription to install premium apps.</summary>
    public const string CanInstallPremiumApps = "CanInstallPremiumApps";

    /// <summary>Requires authentication, the <c>Developer</c> role, and a <c>pro</c> subscription.</summary>
    public const string CanPublishApps = "CanPublishApps";

    /// <summary>Requires a <c>pro</c> subscription and enforces a 1 000 req/hour rate limit.</summary>
    public const string ApiAccess = "ApiAccess";

    /// <summary>Requires an <c>enterprise</c> subscription and enforces a 10 000 req/hour rate limit.</summary>
    public const string ApiAccessEnterprise = "ApiAccessEnterprise";

    // ─────────────────────────────────────────────────────────────────────────
    // Registration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all built-in HPD.Auth policies on <paramref name="options"/>.
    /// </summary>
    /// <remarks>
    /// Called automatically by <c>HPDAuthAuthorizationBuilderExtensions.AddAuthorization()</c>.
    /// Consume via the <see cref="AuthorizationOptions"/> overload:
    /// <code>
    /// services.AddAuthorization(HPDAuthPolicies.RegisterPolicies);
    /// </code>
    /// </remarks>
    public static void RegisterPolicies(AuthorizationOptions options)
    {
        // ── Role-based ──────────────────────────────────────────────────────
        options.AddPolicy(RequireAdmin, p => p.RequireRole("Admin"));

        options.AddPolicy(RequireAdminOrModerator, p => p.RequireRole("Admin", "Moderator"));

        // ── Claim-based ─────────────────────────────────────────────────────
        options.AddPolicy(RequirePremium, p =>
            p.RequireClaim("subscription_tier", "pro", "enterprise"));

        options.AddPolicy(RequireEnterprise, p =>
            p.RequireClaim("subscription_tier", "enterprise"));

        options.AddPolicy(RequireEmailVerified, p =>
            p.RequireClaim(ClaimTypes.Email)
             .RequireAssertion(ctx =>
                 ctx.User.HasClaim(c => c.Type == "email_verified" && c.Value == "true")));

        // ── HPD app system ───────────────────────────────────────────────────
        options.AddPolicy(CanInstallApps, p =>
            p.RequireAuthenticatedUser()
             .AddRequirements(new SubscriptionTierRequirement("free")));

        options.AddPolicy(CanInstallPremiumApps, p =>
            p.RequireAuthenticatedUser()
             .AddRequirements(new SubscriptionTierRequirement("pro")));

        options.AddPolicy(CanPublishApps, p =>
            p.RequireAuthenticatedUser()
             .RequireRole("Developer")
             .AddRequirements(new SubscriptionTierRequirement("pro")));

        // ── API access ───────────────────────────────────────────────────────
        options.AddPolicy(ApiAccess, p =>
            p.RequireAuthenticatedUser()
             .AddRequirements(new SubscriptionTierRequirement("pro"))
             .AddRequirements(new RateLimitRequirement(1000, TimeSpan.FromHours(1))));

        options.AddPolicy(ApiAccessEnterprise, p =>
            p.RequireAuthenticatedUser()
             .AddRequirements(new SubscriptionTierRequirement("enterprise"))
             .AddRequirements(new RateLimitRequirement(10000, TimeSpan.FromHours(1))));
    }
}
