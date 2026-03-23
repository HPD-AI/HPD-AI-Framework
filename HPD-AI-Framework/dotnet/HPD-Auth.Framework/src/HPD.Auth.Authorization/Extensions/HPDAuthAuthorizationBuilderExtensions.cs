using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Middleware;
using HPD.Auth.Authorization.Policies;
using HPD.Auth.Authorization.Services;
using HPD.Auth.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Auth.Authorization.Extensions;

/// <summary>
/// Extension methods on <see cref="IHPDAuthBuilder"/> for registering the
/// HPD authorization stack (policies, requirement handlers, rate-limit service,
/// and the custom middleware result handler).
/// </summary>
/// <remarks>
/// Usage — chain after <c>AddHPDAuth()</c> (and optionally <c>.AddAuthentication()</c>)
/// in <c>Program.cs</c>:
/// <code>
/// services.AddHPDAuth(options => { ... })
///         .AddAuthentication()
///         .AddAuthorization();
/// </code>
///
/// <para>
/// <b>Service contracts that consuming applications must fulfil:</b>
/// <list type="bullet">
///   <item>
///     <see cref="ISubscriptionService"/> — used by <see cref="SubscriptionTierHandler"/>
///     as a fallback when JWT/cookie claims are stale.
///   </item>
///   <item>
///     <see cref="IAppPermissionService"/> — used by <see cref="AppAccessHandler"/>
///     to check app-level access.
///   </item>
///   <item>
///     <see cref="IFeatureFlagService"/> — used by <see cref="FeatureFlagHandler"/>
///     to evaluate feature flags.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Overrideable defaults:</b>
/// <see cref="IRateLimitService"/> is registered as a singleton
/// <see cref="InMemoryRateLimitService"/> for development convenience.
/// Production callers should register a distributed implementation
/// (e.g. Redis-backed) <b>after</b> this call; the later registration will
/// override the in-memory default.
/// </para>
/// </remarks>
public static class HPDAuthAuthorizationBuilderExtensions
{
    /// <summary>
    /// Registers all HPD authorization policies, requirement handlers, the in-memory
    /// rate-limit service (dev default), and the custom 401/403 JSON response handler.
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
        services.AddScoped<IAuthorizationHandler, RateLimitHandler>();
        services.AddScoped<IAuthorizationHandler, FeatureFlagHandler>();

        // ── Rate-limit service (in-memory dev default) ───────────────────────
        // TryAdd so that a production caller registering its own IRateLimitService
        // before or after this call retains control.
        services.TryAddSingleton<IRateLimitService, InMemoryRateLimitService>();

        // ── Custom 401/403 JSON response handler ─────────────────────────────
        services.AddSingleton<IAuthorizationMiddlewareResultHandler,
            HPDAuthorizationMiddlewareResultHandler>();

        return builder;
    }
}
