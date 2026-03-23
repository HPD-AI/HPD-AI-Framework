using System.Security.Claims;
using System.Text.Json;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Core.Options;
using HPD.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Endpoints;

/// <summary>
/// Core auth endpoints: signup, logout, get/update current user.
///
/// Routes registered:
///   POST /api/auth/signup
///   POST /api/auth/logout
///   GET  /api/auth/user
///   PUT  /api/auth/user
/// </summary>
public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/signup", SignUpAsync)
             .WithName("AuthSignUp")
             .WithSummary("Create a new user account. Returns tokens immediately unless RequireEmailConfirmation is true.");

        group.MapPost("/logout", LogoutAsync)
             .WithName("AuthLogout")
             .WithSummary("Sign out the current user. Supports local, global, and others scopes.")
             .RequireAuthorization();

        group.MapGet("/user", GetCurrentUserAsync)
             .WithName("AuthGetUser")
             .WithSummary("Get the currently authenticated user.")
             .RequireAuthorization();

        group.MapPut("/user", UpdateCurrentUserAsync)
             .WithName("AuthUpdateUser")
             .WithSummary("Update mutable profile fields for the currently authenticated user.")
             .RequireAuthorization();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/signup
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> SignUpAsync(
        SignUpRequest request,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IHPDAuthEmailSender emailSender,
        IEventCoordinator eventCoordinator,
        IAuditLogger auditLogger,
        HPDAuthOptions options,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new AuthError("invalid_request", "Email is required."));

        if (string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new AuthError("invalid_request", "Password is required."));

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new AuthError(
                "validation_failed",
                string.Join("; ", result.Errors.Select(e => e.Description))));
        }

        // Assign default "User" role.
        await userManager.AddToRoleAsync(user, "User");

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        eventCoordinator.Emit(new UserRegisteredEvent
        {
            UserId = user.Id,
            Email = user.Email!,
            RegistrationMethod = "email",
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent },
        });

        // If email confirmation is required, send the confirmation email and
        // return 200 with a message (no tokens yet).
        if (options.Features.RequireEmailConfirmation)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), token, ct);

            return Results.Ok(new { message = "Registration successful. Please check your email to confirm your account." });
        }

        // Email confirmation not required — issue tokens immediately.
        var tokenResponse = await tokenService.GenerateTokensAsync(user, ct);
        return Results.Ok(tokenResponse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/logout
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> LogoutAsync(
        LogoutRequest? request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        ISessionManager sessionManager,
        IEventCoordinator eventCoordinator,
        HttpContext httpContext,
        string? scope = null,
        CancellationToken ct = default)
    {
        var effectiveScope = scope ?? request?.Scope ?? "local";
        var refreshToken = request?.RefreshToken;

        // Revoke refresh token if provided.
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await tokenService.RevokeAsync(refreshToken, ct);

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        if (userId is not null && Guid.TryParse(userId, out var userGuid))
        {
            var user = await userManager.FindByIdAsync(userId);

            switch (effectiveScope.ToLowerInvariant())
            {
                case "global":
                    if (user is not null)
                        await userManager.UpdateSecurityStampAsync(user);
                    await tokenService.RevokeAllForUserAsync(userGuid, ct);
                    break;

                case "others":
                    var currentSessionIdClaim = principal.FindFirstValue("session_id");
                    Guid? currentSessionId = currentSessionIdClaim is not null && Guid.TryParse(currentSessionIdClaim, out var sid)
                        ? sid
                        : null;
                    await sessionManager.RevokeAllSessionsAsync(userGuid, currentSessionId, ct);
                    await tokenService.RevokeAllForUserAsync(userGuid, ct);
                    break;

                default: // "local"
                    break;
            }

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            eventCoordinator.Emit(new UserLoggedOutEvent
            {
                UserId = userGuid,
                SessionId = Guid.Empty,
                AuthContext = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent },
            });
        }

        // Sign out cookie session.
        await signInManager.SignOutAsync();

        return Results.Ok(new { message = "Logout successful." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/auth/user
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        if (userId is null)
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Results.NotFound(new AuthError("user_not_found", "User not found."));

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToUserResponse(user, roles));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/auth/user
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> UpdateCurrentUserAsync(
        UpdateUserRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        if (userId is null)
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Results.NotFound(new AuthError("user_not_found", "User not found."));

        if (request.FirstName is not null)
            user.FirstName = request.FirstName;

        if (request.LastName is not null)
            user.LastName = request.LastName;

        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName;

        if (request.UserMetadata is not null)
            user.UserMetadata = request.UserMetadata;

        user.Updated = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new AuthError(
                "validation_failed",
                string.Join("; ", result.Errors.Select(e => e.Description))));
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserUpdate,
            Category: AuditCategories.UserManagement,
            Success: true,
            UserId: user.Id,
            IpAddress: ipAddress,
            Metadata: new { action = "user_self_update" }
        ), ct);

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToUserResponse(user, roles));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    internal static UserTokenDto ToUserResponse(ApplicationUser user, IList<string>? roles = null)
    {
        return new UserTokenDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            EmailConfirmedAt = user.EmailConfirmedAt,
            UserMetadata = ParseJsonElement(user.UserMetadata),
            AppMetadata = ParseJsonElement(user.AppMetadata),
            RequiredActions = user.RequiredActions,
            CreatedAt = user.Created,
            SubscriptionTier = user.SubscriptionTier,
        };
    }

    private static JsonElement ParseJsonElement(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement;
        }
        catch
        {
            return JsonDocument.Parse("{}").RootElement;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>POST /api/auth/signup request body.</summary>
public record SignUpRequest(
    string Email,
    string Password,
    string? FirstName = null,
    string? LastName = null
);

/// <summary>POST /api/auth/logout request body.</summary>
public record LogoutRequest(
    string? Scope = "local",
    string? RefreshToken = null
);

/// <summary>PUT /api/auth/user request body.</summary>
public record UpdateUserRequest(
    string? FirstName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? UserMetadata = null
);

/// <summary>
/// Standard OAuth 2.0 / RFC 6749 error response. Shared across all auth endpoints.
/// </summary>
public record AuthError(
    string Error,
    string ErrorDescription,
    string? ErrorUri = null
);
