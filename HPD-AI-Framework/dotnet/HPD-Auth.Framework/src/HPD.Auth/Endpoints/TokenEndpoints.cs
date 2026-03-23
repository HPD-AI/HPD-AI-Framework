using System.Security.Claims;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

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
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/token", TokenAsync)
           .WithName("AuthToken")
           .WithSummary("OAuth 2.0 token endpoint. Supports grant_type=password and grant_type=refresh_token.");
    }

    private static async Task<IResult> TokenAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        IAuditLogger auditLogger,
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
                body = await httpContext.Request.ReadFromJsonAsync<TokenRequest>(ct);
            }
            catch
            {
                return Results.BadRequest(new AuthError("invalid_request", "Malformed request body."));
            }

            if (body is null)
                return Results.BadRequest(new AuthError("invalid_request", "Request body is required."));

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

        return grantType switch
        {
            "password"      => await HandlePasswordGrantAsync(
                                   username, password, userManager, signInManager,
                                   tokenService, eventCoordinator, auditLogger,
                                   ipAddress, userAgent, ct),
            "refresh_token" => await HandleRefreshGrantAsync(
                                   refreshToken, tokenService, auditLogger,
                                   ipAddress, userAgent, ct),
            _ => Results.BadRequest(new AuthError("unsupported_grant_type",
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
        IAuditLogger auditLogger,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.BadRequest(new AuthError(
                "invalid_request",
                "username (email) and password are required for grant_type=password."));
        }

        var authContext = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent };

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            eventCoordinator.Emit(new LoginFailedEvent
            {
                Email = email,
                Reason = "user_not_found",
                AuthContext = authContext,
            });
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.UserLoginFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                IpAddress: ipAddress,
                UserAgent: userAgent,
                ErrorMessage: "user_not_found",
                Metadata: new { email }
            ), ct);

            return Results.BadRequest(new AuthError("invalid_grant", "Invalid email or password."));
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            eventCoordinator.Emit(new LoginFailedEvent
            {
                Email = email,
                Reason = "account_locked",
                AuthContext = authContext,
            });
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.UserLoginFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                UserId: user.Id,
                IpAddress: ipAddress,
                UserAgent: userAgent,
                ErrorMessage: "account_locked",
                Metadata: new { email }
            ), ct);

            return Results.StatusCode(423);
        }

        if (!signInResult.Succeeded)
        {
            eventCoordinator.Emit(new LoginFailedEvent
            {
                Email = email,
                Reason = "invalid_password",
                AuthContext = authContext,
            });
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.UserLoginFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                UserId: user.Id,
                IpAddress: ipAddress,
                UserAgent: userAgent,
                ErrorMessage: "invalid_password",
                Metadata: new { email }
            ), ct);

            return Results.BadRequest(new AuthError("invalid_grant", "Invalid email or password."));
        }

        if (await signInManager.IsTwoFactorEnabledAsync(user))
        {
            return Results.Ok(new { requires_two_factor = true });
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress;
        user.Updated = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        var tokenResponse = await tokenService.GenerateTokensAsync(user, ct);

        eventCoordinator.Emit(new UserLoggedInEvent
        {
            UserId = user.Id,
            Email = user.Email!,
            AuthMethod = "password",
            AuthContext = authContext,
        });

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.UserLogin,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: ipAddress,
            UserAgent: userAgent,
            Metadata: new { email, auth_method = "password" }
        ), ct);

        return Results.Ok(tokenResponse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // grant_type=refresh_token
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleRefreshGrantAsync(
        string? refreshToken,
        ITokenService tokenService,
        IAuditLogger auditLogger,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Results.BadRequest(new AuthError(
                "invalid_request",
                "refresh_token is required for grant_type=refresh_token."));
        }

        var tokenResponse = await tokenService.RefreshAsync(refreshToken, ct);
        if (tokenResponse is null)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.TokenRefreshFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                IpAddress: ipAddress,
                UserAgent: userAgent,
                ErrorMessage: "invalid_or_expired_refresh_token"
            ), ct);

            return Results.BadRequest(new AuthError(
                "invalid_grant",
                "The refresh token is invalid, expired, or has already been used."));
        }

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.TokenRefresh,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: tokenResponse.User.Id,
            IpAddress: ipAddress,
            UserAgent: userAgent
        ), ct);

        return Results.Ok(tokenResponse);
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
