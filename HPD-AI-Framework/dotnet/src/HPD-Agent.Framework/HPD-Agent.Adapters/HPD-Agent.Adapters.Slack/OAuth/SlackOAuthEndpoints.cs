using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Slack.OAuth;

/// <summary>
/// Maps the two Slack OAuth 2.0 endpoints onto an <see cref="IEndpointRouteBuilder"/>:
/// <list type="bullet">
///   <item><c>GET {installPath}</c> — redirects the browser to Slack's OAuth consent screen.</item>
///   <item><c>GET {callbackPath}</c> — exchanges the <c>code</c> for a bot token and stores it.</item>
/// </list>
/// </summary>
public static class SlackOAuthEndpoints
{
    /// <summary>
    /// Maps <c>GET /slack/install</c> and <c>GET /slack/oauth/callback</c>
    /// (or custom paths supplied by the caller).
    /// </summary>
    public static IEndpointRouteBuilder MapSlackOAuth(
        this IEndpointRouteBuilder app,
        string installPath  = "/slack/install",
        string callbackPath = "/slack/oauth/callback")
    {
        // ── Install: redirect to Slack consent URL ──────────────────────────────
        app.MapGet(installPath, (IOptions<SlackOAuthConfig> opts) =>
        {
            var cfg   = opts.Value;
            var scope = string.Join(",", cfg.Scopes);
            var url   = $"https://slack.com/oauth/v2/authorize"
                      + $"?client_id={Uri.EscapeDataString(cfg.ClientId)}"
                      + $"&scope={Uri.EscapeDataString(scope)}"
                      + $"&redirect_uri={Uri.EscapeDataString(cfg.RedirectUri)}";
            return Results.Redirect(url);
        });

        // ── Callback: exchange code for token ───────────────────────────────────
        app.MapGet(callbackPath, async (
            HttpContext ctx,
            IOptions<SlackOAuthConfig> opts,
            ISlackTokenStore tokenStore,
            IHttpClientFactory httpClientFactory,
            ILogger<SlackOAuthCallbackMarker> logger,
            CancellationToken ct) =>
        {
            var cfg  = opts.Value;
            var code = ctx.Request.Query["code"].ToString();

            if (string.IsNullOrEmpty(code))
            {
                var error = ctx.Request.Query["error"].ToString();
                logger.LogWarning("Slack OAuth denied or missing code. error={Error}", error);
                return Results.Text("OAuth flow was cancelled or denied.", statusCode: 400);
            }

            // Exchange code for token via oauth.v2.access
            var http = httpClientFactory.CreateClient("slack");
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = cfg.ClientId,
                ["client_secret"] = cfg.ClientSecret,
                ["code"]          = code,
                ["redirect_uri"]  = cfg.RedirectUri,
            });

            var response = await http.PostAsync("https://slack.com/api/oauth.v2.access", form, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(
                SlackOAuthJsonContext.Default.SlackOAuthV2Response, ct);

            if (result is null || !result.Ok)
            {
                logger.LogError("Slack oauth.v2.access failed. error={Error}", result?.Error);
                return Results.Text("Slack OAuth exchange failed: " + result?.Error, statusCode: 500);
            }

            var teamId   = result.Team?.Id ?? string.Empty;
            var botToken = result.AccessToken ?? string.Empty;

            if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(botToken))
            {
                logger.LogError("Slack oauth.v2.access returned empty team_id or access_token.");
                return Results.Text("Slack OAuth returned incomplete data.", statusCode: 500);
            }

            await tokenStore.SaveAsync(teamId, botToken, ct);
            logger.LogInformation("Slack app installed for team {TeamId}.", teamId);

            var redirect = cfg.PostInstallRedirectUri ?? "/";
            return Results.Redirect(redirect);
        });

        return app;
    }
}

/// <summary>Marker type used to scope the OAuth callback logger category.</summary>
internal sealed class SlackOAuthCallbackMarker;
