using System.Security.Claims;
using System.Text.Json.Serialization;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Core.Options;
using HPD.Auth.Serialization;
using HPD.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Endpoints;

/// <summary>
/// OAuth 2.0 token endpoint.
///
/// Routes registered:
///   POST /api/auth/token  (grant_type=password | grant_type=refresh_token)
///
/// Accepts both JSON body (Content-Type: application/json) and
/// form-encoded body (Content-Type: application/x-www-form-urlencoded).
/// </summary>
public static class TokenEndpoints
{
    /// <summary>
    /// Maps the OAuth 2.0-compatible token endpoint.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/token", TokenRequestDelegate)
           .WithName("AuthToken")
           .WithSummary("OAuth 2.0 token endpoint. Supports grant_type=password and grant_type=refresh_token.");
    }

    private static async Task TokenRequestDelegate(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var result = await TokenAsync(
            httpContext,
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<SignInManager<ApplicationUser>>(),
            services.GetRequiredService<ITokenService>(),
            services.GetRequiredService<IEventCoordinator>(),
            services.GetRequiredService<HPDAuthOptions>(),
            httpContext.RequestAborted);

        await result.ExecuteAsync(httpContext);
    }

    private static async Task<IResult> TokenAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        HPDAuthOptions options,
        CancellationToken ct = default)
    {
        // Support both JSON body and form-encoded body.
        string? grantType;
        string? username;
        string? password;
        string? refreshToken;

        var contentType = httpContext.Request.ContentType ?? string.Empty;

        if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var form = await httpContext.Request.ReadFormAsync(ct);
            grantType = form["grant_type"].FirstOrDefault();
            username = form["username"].FirstOrDefault();
            password = form["password"].FirstOrDefault();
            refreshToken = form["refresh_token"].FirstOrDefault();
        }
        else
        {
            TokenRequest? body;
            try
            {
                body = await AuthEndpointJson.ReadJsonAsync(
                    httpContext,
                    HPDAuthJsonSerializerContext.Default.TokenRequest,
                    ct);
            }
            catch
            {
                return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Malformed request body."));
            }

            if (body is null)
                return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Request body is required."));

            grantType = body.GrantType ?? httpContext.Request.Query["grant_type"].FirstOrDefault();
            username = body.Username ?? body.Email;
            password = body.Password;
            refreshToken = body.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(grantType))
        {
            grantType = httpContext.Request.Query["grant_type"].FirstOrDefault();
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        TokenIssuanceIdentity issuanceIdentity = TokenIssuanceIdentityHttp.Create(
            httpContext,
            grantType == "password" ? "auth.token.password" : "auth.token.refresh");

        return grantType switch
        {
            "password"      => await HandlePasswordGrantAsync(
                                   username, password, userManager, signInManager,
                                   tokenService, eventCoordinator,
                                   ipAddress, userAgent, issuanceIdentity, ct),
            "refresh_token" => await HandleRefreshGrantAsync(
                                   refreshToken, tokenService,
                                   ipAddress, userAgent, ct),
            _ => AuthEndpointJson.BadRequest(new AuthError("unsupported_grant_type",
                $"grant_type '{grantType}' is not supported. Use 'password' or 'refresh_token'."))
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // grant_type=password
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandlePasswordGrantAsync(
        string? email,
        string? password,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        string? ipAddress,
        string? userAgent,
        TokenIssuanceIdentity issuanceIdentity,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return AuthEndpointJson.BadRequest(new AuthError(
                "invalid_request",
                "username (email) and password are required for grant_type=password."));
        }

        var authContext = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent };

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            await eventCoordinator.EmitAsync(new LoginFailedEvent
            {
                Email = email,
                Reason = "user_not_found",
                AuthContext = authContext,
            }, ct);
            return AuthEndpointJson.BadRequest(new AuthError("invalid_grant", "Invalid email or password."));
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            await eventCoordinator.EmitAsync(new LoginFailedEvent
            {
                Email = email,
                Reason = "account_locked",
                AuthContext = authContext,
            }, ct);
            return Results.StatusCode(423);
        }

        if (signInResult.IsNotAllowed)
        {
            await eventCoordinator.EmitAsync(new LoginFailedEvent
            {
                Email = email,
                Reason = "not_allowed",
                AuthContext = authContext,
            }, ct);
            return AuthEndpointJson.BadRequest(new AuthError(
                "invalid_grant",
                "Email confirmation is required before login."));
        }

        if (!signInResult.Succeeded)
        {
            await eventCoordinator.EmitAsync(new LoginFailedEvent
            {
                Email = email,
                Reason = "invalid_password",
                AuthContext = authContext,
            }, ct);
            return AuthEndpointJson.BadRequest(new AuthError("invalid_grant", "Invalid email or password."));
        }

        if (await signInManager.IsTwoFactorEnabledAsync(user))
        {
            return AuthEndpointJson.Ok(
                new TwoFactorRequiredResponse(true),
                HPDAuthJsonSerializerContext.Default.TwoFactorRequiredResponse);
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress;
        user.Updated = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        var tokenResponse = await tokenService.GenerateTokensAsync(
            user,
            issuanceIdentity,
            ct);

        await eventCoordinator.EmitAsync(new UserLoggedInEvent
        {
            UserId = user.Id,
            Email = user.Email!,
            AuthMethod = "password",
            AuthContext = authContext,
        }, ct);

        return AuthEndpointJson.Ok(tokenResponse, HPDAuthJsonSerializerContext.Default.TokenResponse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // grant_type=refresh_token
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleRefreshGrantAsync(
        string? refreshToken,
        ITokenService tokenService,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthEndpointJson.BadRequest(new AuthError(
                "invalid_request",
                "refresh_token is required for grant_type=refresh_token."));
        }

        var tokenResponse = await tokenService.RefreshAsync(refreshToken, ct);
        if (tokenResponse is null)
        {
            return AuthEndpointJson.BadRequest(new AuthError(
                "invalid_grant",
                "The refresh token is invalid, expired, or has already been used."));
        }

        return AuthEndpointJson.Ok(tokenResponse, HPDAuthJsonSerializerContext.Default.TokenResponse);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// JSON body for POST /api/auth/token.
/// Form-encoded requests are also accepted using the same field names (snake_case).
/// </summary>
internal sealed class TokenRequest
{
    public string? GrantType { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }    // convenience alias for username
    public string? Password { get; set; }
    public string? RefreshToken { get; set; }
}

/// <summary>Response returned when password auth must continue through 2FA.</summary>
internal sealed record TwoFactorRequiredResponse(
    [property: JsonPropertyName("requires_two_factor")] bool RequiresTwoFactor);
