using AspNet.Security.OAuth.GitHub;
using HPD.Auth.Builder;
using HPD.Auth.Core.Options;
using HPD.Auth.OAuth.Endpoints;
using HPD.Auth.OAuth.Handlers;
using HPD.Auth.OAuth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.OAuth.Extensions;

/// <summary>
/// Extension methods on <see cref="IHPDAuthBuilder"/> for wiring up the HPD OAuth package.
///
/// <para>
/// Usage — chain after <c>AddHPDAuth()</c> and <c>AddAuthentication()</c> in <c>Program.cs</c>:
/// <code>
/// services.AddHPDAuth(options =>
/// {
///     options.OAuth.Providers["google"] = new OAuthProviderOptions
///     {
///         ClientId     = configuration["Auth:Google:ClientId"]!,
///         ClientSecret = configuration["Auth:Google:ClientSecret"]!,
///     };
///     options.OAuth.Providers["github"] = new OAuthProviderOptions
///     {
///         ClientId     = configuration["Auth:GitHub:ClientId"]!,
///         ClientSecret = configuration["Auth:GitHub:ClientSecret"]!,
///     };
/// })
/// .AddAuthentication()   // from HPD.Auth.Authentication
/// .AddOAuth();           // this extension — registers schemes + handler
/// </code>
/// </para>
///
/// <para>
/// To mount the two OAuth endpoints (<c>GET /auth/{provider}</c> and
/// <c>GET /auth/{provider}/callback</c>) call the companion extension on the built
/// <see cref="IEndpointRouteBuilder"/>:
/// <code>
/// app.MapHPDOAuthEndpoints();
/// </code>
/// </para>
///
/// <para>
/// <b>Ordering note</b>: <c>AddOAuth()</c> must be called after <c>AddAuthentication()</c>
/// because it chains onto the <see cref="AuthenticationBuilder"/> that
/// <c>AddAuthentication()</c> registered. Calling it before will throw at runtime.
/// </para>
/// </summary>
public static class HPDAuthOAuthBuilderExtensions
{
    /// <summary>
    /// Registers OAuth services and wires up provider authentication schemes for every
    /// provider that has a non-empty <c>ClientId</c> in <see cref="OAuthOptions.Providers"/>.
    ///
    /// <para>
    /// Registered services:
    /// <list type="bullet">
    ///   <item><see cref="ExternalLoginHandler"/> (scoped) — callback + provisioning logic.</item>
    ///   <item><see cref="ExternalProviderService"/> (scoped) — claim normalisation.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Registered authentication schemes (conditional on configuration):
    /// <list type="bullet">
    ///   <item>Google — when <c>options.OAuth.Providers["google"].ClientId</c> is set.</item>
    ///   <item>GitHub — when <c>options.OAuth.Providers["github"].ClientId</c> is set.</item>
    ///   <item>Microsoft — when <c>options.OAuth.Providers["microsoft"].ClientId</c> is set.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="builder">The fluent builder returned by <c>AddHPDAuth()</c>.</param>
    /// <returns>The same <paramref name="builder"/> for further chaining.</returns>
    public static IHPDAuthBuilder AddOAuth(this IHPDAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var options  = builder.Options;

        // ── 1. Register scoped services ───────────────────────────────────────
        services.AddScoped<ExternalLoginHandler>();
        services.AddScoped<ExternalProviderService>();

        // ── 2. Register OAuth provider schemes ────────────────────────────────
        // Retrieve the IAuthenticationBuilder that AddAuthentication() registered.
        // OAuthSchemeRegistrar chains the individual AddGoogle / AddGitHub / AddMicrosoftAccount
        // calls onto it, conditional on the HPDAuthOptions configuration.
        var authBuilder = new AuthenticationBuilder(services);
        OAuthSchemeRegistrar.RegisterSchemes(authBuilder, options.OAuth);

        return builder;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MapHPDOAuthEndpoints — IEndpointRouteBuilder extension
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mounts the OAuth challenge and callback Minimal API endpoints:
    /// <list type="bullet">
    ///   <item><c>GET /auth/{provider}</c></item>
    ///   <item><c>GET /auth/{provider}/callback</c></item>
    /// </list>
    ///
    /// <para>
    /// Call this on the built <c>WebApplication</c> after
    /// <c>app.UseAuthentication()</c> and <c>app.UseAuthorization()</c>:
    /// <code>
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.MapHPDOAuthEndpoints();
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="app">The endpoint route builder (e.g., <c>WebApplication</c>).</param>
    /// <returns>The same <paramref name="app"/> for further chaining.</returns>
    public static IEndpointRouteBuilder MapHPDOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        OAuthEndpoints.Map(app);
        return app;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// OAuthSchemeRegistrar — internal helper for Phase 4 wiring
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Chains the provider-specific <c>AddGoogle</c> / <c>AddGitHub</c> /
/// <c>AddMicrosoftAccount</c> calls onto an existing <see cref="AuthenticationBuilder"/>.
///
/// <para>
/// This class is <c>internal</c> so that it does not leak ASP.NET Core authentication
/// builder types into the public API surface of the HPD.Auth.OAuth package. The
/// <c>HPDAuthOAuthBuilderExtensions.AddOAuth</c> method calls it; host applications do
/// not need to interact with it directly.
/// </para>
///
/// <para>
/// A provider scheme is only registered when the corresponding entry in
/// <see cref="OAuthOptions.Providers"/> exists, is enabled, and has a non-empty
/// <c>ClientId</c>. This avoids "scheme already registered" exceptions in test
/// environments where only a subset of providers is configured.
/// </para>
/// </summary>
internal static class OAuthSchemeRegistrar
{
    /// <summary>
    /// Registers OAuth authentication schemes for every configured and enabled
    /// provider in <paramref name="oauthOptions"/>.
    /// </summary>
    /// <param name="authBuilder">
    /// The <see cref="AuthenticationBuilder"/> to chain the provider registrations onto.
    /// </param>
    /// <param name="oauthOptions">
    /// The <see cref="OAuthOptions"/> instance from <see cref="HPDAuthOptions"/>.
    /// Provider configuration is read from <see cref="OAuthOptions.Providers"/>.
    /// </param>
    /// <returns>The same <paramref name="authBuilder"/> for further chaining.</returns>
    internal static AuthenticationBuilder RegisterSchemes(
        AuthenticationBuilder authBuilder,
        OAuthOptions oauthOptions)
    {
        ArgumentNullException.ThrowIfNull(authBuilder);
        ArgumentNullException.ThrowIfNull(oauthOptions);

        // ── Google ────────────────────────────────────────────────────────────
        if (oauthOptions.Providers.TryGetValue("google", out var googleOpts)
            && googleOpts.Enabled
            && !string.IsNullOrEmpty(googleOpts.ClientId))
        {
            authBuilder.AddGoogle(o =>
            {
                o.ClientId     = googleOpts.ClientId;
                o.ClientSecret = googleOpts.ClientSecret;

                // Map the "picture" claim so AvatarUrl can be populated.
                o.ClaimActions.MapJsonKey("picture", "picture");

                // Request additional scopes beyond the openid/email/profile defaults.
                foreach (var scope in googleOpts.AdditionalScopes)
                    o.Scope.Add(scope);

                // Override the callback path if the caller has specified one.
                if (!string.IsNullOrEmpty(googleOpts.CallbackPath))
                    o.CallbackPath = googleOpts.CallbackPath;

                o.SaveTokens = true;
            });
        }

        // ── GitHub ────────────────────────────────────────────────────────────
        if (oauthOptions.Providers.TryGetValue("github", out var githubOpts)
            && githubOpts.Enabled
            && !string.IsNullOrEmpty(githubOpts.ClientId))
        {
            authBuilder.AddGitHub(o =>
            {
                o.ClientId     = githubOpts.ClientId;
                o.ClientSecret = githubOpts.ClientSecret;

                // AspNet.Security.OAuth.GitHub maps avatar_url to the
                // "urn:github:avatar" claim by default.
                foreach (var scope in githubOpts.AdditionalScopes)
                    o.Scope.Add(scope);

                if (!string.IsNullOrEmpty(githubOpts.CallbackPath))
                    o.CallbackPath = githubOpts.CallbackPath;

                o.SaveTokens = true;
            });
        }

        // ── Microsoft Account ─────────────────────────────────────────────────
        if (oauthOptions.Providers.TryGetValue("microsoft", out var msOpts)
            && msOpts.Enabled
            && !string.IsNullOrEmpty(msOpts.ClientId))
        {
            authBuilder.AddMicrosoftAccount(o =>
            {
                o.ClientId     = msOpts.ClientId;
                o.ClientSecret = msOpts.ClientSecret;

                foreach (var scope in msOpts.AdditionalScopes)
                    o.Scope.Add(scope);

                if (!string.IsNullOrEmpty(msOpts.CallbackPath))
                    o.CallbackPath = msOpts.CallbackPath;

                o.SaveTokens = true;
            });
        }

        return authBuilder;
    }
}
