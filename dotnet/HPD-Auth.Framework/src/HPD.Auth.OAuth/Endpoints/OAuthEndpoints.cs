using HPD.Auth.Core.Interfaces;
using HPD.Auth.OAuth.Handlers;
using HPD.Auth.OAuth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.OAuth.Endpoints;

/// <summary>
/// Minimal API endpoint definitions for the OAuth authentication flow.
///
/// <para>
/// Two endpoints are registered:
/// <list type="bullet">
///   <item>
///     <c>GET /auth/{provider}</c> — Issues a challenge redirect to the specified OAuth
///     provider. The <c>provider</c> path segment is normalised to the canonical scheme
///     name (e.g., <c>"google"</c> → <c>"Google"</c>). Unknown providers receive a 400.
///   </item>
///   <item>
///     <c>GET /auth/{provider}/callback</c> — Handles the authorization-code callback
///     from the provider. Delegates to <see cref="ExternalLoginHandler.HandleCallbackAsync"/>.
///     On success the response depends on the <c>useCookies</c> query flag:
///     <list type="bullet">
///       <item>
///         <c>useCookies=true</c> (default) — The user was already signed in by
///         <c>SignInManager.ExternalLoginSignInAsync</c>; redirect to <c>returnUrl</c>.
///       </item>
///       <item>
///         <c>useCookies=false</c> — Return a JSON <c>TokenResponse</c> (for native
///         API clients).
///       </item>
///     </list>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Call <see cref="Map"/> from <c>HPDAuthOAuthBuilderExtensions.MapHPDOAuthEndpoints</c>
/// or directly from <c>app.MapHPDOAuthEndpoints()</c> in <c>Program.cs</c>.
/// </para>
/// </summary>
public static class OAuthEndpoints
{
    /// <summary>
    /// Registers the OAuth challenge and callback endpoints on the supplied
    /// <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        // ── GET /auth/{provider} ───────────────────────────────────────────────
        // Initiates the OAuth flow by issuing a Challenge to the named scheme.
        // The browser is redirected to the provider's authorization URL.
        app.MapGet("/auth/{provider}", HandleChallenge)
           .AllowAnonymous()
           .WithName("OAuthChallenge")
           .WithTags("OAuth")
           .WithSummary("Redirect to an OAuth provider for authentication");

        // ── GET /auth/{provider}/callback ──────────────────────────────────────
        // Called by the provider after the user approves (or denies) access.
        // The OAuth middleware intercepts at the internal /signin-{provider} path
        // first, exchanges the code for tokens, then redirects here.
        app.MapGet("/auth/{provider}/callback", HandleCallback)
           .AllowAnonymous()
           .WithName("OAuthCallback")
           .WithTags("OAuth")
           .WithSummary("Handle the OAuth provider callback and sign the user in");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Challenge handler
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleChallenge(
        string provider,
        HttpContext ctx,
        ExternalProviderService providerService,
        string? returnUrl = null,
        bool useCookies = true)
    {
        // Validate and normalise the provider name from the URL segment.
        var schemeName = providerService.NormalizeProviderName(provider);
        if (schemeName is null)
        {
            return Results.Problem(
                detail: $"'{provider}' is not a supported OAuth provider.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsupported provider");
        }

        // The callback URL that the OAuth middleware will redirect to after the
        // provider returns.  We include returnUrl and useCookies so the callback
        // handler can pick them up from the query string.
        var callbackUrl = $"/auth/{provider}/callback"
            + $"?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}"
            + $"&useCookies={useCookies}";

        var properties = new AuthenticationProperties
        {
            RedirectUri = callbackUrl,
            Items =
            {
                // Store auth mode so UpsertUserIdentityAsync and audit logging
                // know how the session was initiated.
                ["auth_mode"] = useCookies ? "cookie" : "jwt",
                ["returnUrl"] = returnUrl ?? "/",
            },
        };

        // Challenge returns HTTP 302 to the provider's authorization endpoint.
        await ctx.ChallengeAsync(schemeName, properties);
        return Results.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Callback handler
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleCallback(
        string provider,
        HttpContext ctx,
        ExternalLoginHandler handler,
        ITokenService tokenService,
        ExternalProviderService providerService,
        CancellationToken ct,
        string? returnUrl = null,
        bool useCookies = true,
        string? remoteError = null)
    {
        // Surface provider-reported errors immediately (e.g., user denied access).
        if (!string.IsNullOrEmpty(remoteError))
        {
            var encoded = Uri.EscapeDataString($"Provider error: {remoteError}");
            return Results.Redirect($"/auth/error?message={encoded}");
        }

        // Validate provider name — prevents crafted scheme names from reaching
        // the authentication pipeline.
        var schemeName = providerService.NormalizeProviderName(provider);
        if (schemeName is null)
        {
            return Results.Problem(
                detail: $"'{provider}' is not a supported OAuth provider.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsupported provider");
        }

        // Delegate to ExternalLoginHandler for the full sign-in / provisioning logic.
        var result = await handler.HandleCallbackAsync(ctx, ct);

        if (!result.IsSuccess)
        {
            // Special case: account requires 2FA before full sign-in.
            if (result.ErrorMessage == "requires_two_factor")
            {
                var encodedReturnUrl = Uri.EscapeDataString(returnUrl ?? "/");
                return Results.Redirect($"/auth/2fa?returnUrl={encodedReturnUrl}&useCookies={useCookies}");
            }

            // General failure — redirect to the error page with the message.
            var encodedError = Uri.EscapeDataString(result.ErrorMessage ?? "Login failed");
            return Results.Redirect($"/auth/error?message={encodedError}");
        }

        // Success path.
        if (useCookies)
        {
            // SignInManager.ExternalLoginSignInAsync (called inside HandleCallbackAsync)
            // already wrote the authentication cookie when it succeeded.
            // Just redirect to the originally-requested URL.
            var safeReturnUrl = IsLocalUrl(returnUrl) ? returnUrl! : "/";
            return Results.Redirect(safeReturnUrl);
        }
        else
        {
            // JWT mode — issue a token pair and return it as JSON.
            // Used by native API clients that cannot use cookies.
            var tokens = await tokenService.GenerateTokensAsync(result.User!, ct);
            return Results.Ok(tokens);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Guards against open-redirect attacks by rejecting absolute URLs that
    /// point outside the current application.
    /// </summary>
    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        url.StartsWith('/') &&
        !url.StartsWith("//") &&
        !url.StartsWith("/\\");
}
