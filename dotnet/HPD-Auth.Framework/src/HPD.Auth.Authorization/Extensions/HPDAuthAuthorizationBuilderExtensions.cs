using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Policies;
using HPD.Auth.Authorization.Services;
using HPD.Auth.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Auth.Authorization.Extensions;

/// <summary>
/// Extension methods on <see cref="IHPDAuthBuilder"/> for registering the
/// HPD authorization stack (policies and product requirement handlers).
/// </summary>
/// <remarks>
/// Usage — chain after <c>AddHPDAuth()</c> (and optionally <c>.AddAuthentication()</c>)
/// in <c>Program.cs</c>:
/// <code>
/// services.AddHPDAuth(options => { ... })
///         .UseSqlite(connectionString)
///         .AddAuthentication()
///         .AddAuthorization();
/// </code>
///
/// <para>
/// <b>Overrideable authorization services:</b>
/// <list type="bullet">
///   <item>
///     <see cref="ISubscriptionService"/> — used by <see cref="SubscriptionTierHandler"/>
///     as a fallback when JWT/cookie claims are stale. Defaults to no subscription.
///   </item>
///   <item>
///     <see cref="IAppPermissionService"/> — used by <see cref="AppAccessHandler"/>
///     to check app-level access. Defaults to denying app access.
///   </item>
///   <item>
///     <see cref="IFeatureFlagService"/> — used by <see cref="FeatureFlagHandler"/>
///     to evaluate feature flags. Defaults to all flags disabled.
///   </item>
/// </list>
/// </para>
///
/// </remarks>
public static class HPDAuthAuthorizationBuilderExtensions
{
    /// <summary>
    /// Registers HPD authorization policies and product requirement handlers.
    /// </summary>
    /// <param name="builder">The fluent builder returned by <c>AddHPDAuth()</c>.</param>
    /// <returns>The same <paramref name="builder"/> for further chaining.</returns>
    public static IHPDAuthBuilder AddAuthorization(this IHPDAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        // ── Policies ────────────────────────────────────────────────────────
        services.AddAuthorization(HPDAuthPolicies.RegisterPolicies);

        // ── Requirement handlers ─────────────────────────────────────────────
        services.AddScoped<IAuthorizationHandler, AppAccessHandler>();
        services.AddScoped<IAuthorizationHandler, ResourceOwnerHandler>();
        services.AddScoped<IAuthorizationHandler, SubscriptionTierHandler>();
        services.AddScoped<IAuthorizationHandler, FeatureFlagHandler>();

        // ── Overrideable service defaults ───────────────────────────────────
        // ASP.NET Core resolves all registered IAuthorizationHandler instances
        // when any authorization policy runs, including plain RequireAuthorization().
        // These defaults keep optional handlers resolvable while still failing
        // closed for app access, feature flags, and subscription fallbacks.
        services.TryAddScoped<IAppPermissionService, DenyAllAppPermissionService>();
        services.TryAddScoped<ISubscriptionService, EmptySubscriptionService>();
        services.TryAddScoped<IFeatureFlagService, DisabledFeatureFlagService>();

        return builder;
    }
}
