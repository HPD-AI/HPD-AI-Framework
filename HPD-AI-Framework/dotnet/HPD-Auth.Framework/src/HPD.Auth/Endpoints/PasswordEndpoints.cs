using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;

namespace HPD.Auth.Endpoints;

/// <summary>
/// Password recovery and verification endpoints.
///
/// Routes registered:
///   POST /api/auth/recover  — request password reset email
///   POST /api/auth/verify   — verify OTP (type=recovery|signup|email_change)
///   POST /api/auth/resend   — resend verification / confirmation email
/// </summary>
public static class PasswordEndpoints
{
    private const int ResendCooldownMinutes = 5;

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/recover", RecoverAsync)
             .WithName("AuthRecover")
             .WithSummary("Request a password reset email. Always returns 200 to prevent email enumeration.");

        group.MapPost("/verify", VerifyAsync)
             .WithName("AuthVerify")
             .WithSummary("Verify an OTP token. type=recovery resets password; type=signup confirms email.");

        group.MapPost("/resend", ResendAsync)
             .WithName("AuthResend")
             .WithSummary("Resend a verification/confirmation email. Rate-limited to one request per 5 minutes.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/recover
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> RecoverAsync(
        RecoverRequest request,
        UserManager<ApplicationUser> userManager,
        IHPDAuthEmailSender emailSender,
        IEventCoordinator eventCoordinator,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        const string successMessage = "If your email is registered, you will receive a reset link.";

        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.Ok(new { message = successMessage });

        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendPasswordResetAsync(user.Email!, user.Id.ToString(), token, ct);

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

            eventCoordinator.Emit(new PasswordResetRequestedEvent
            {
                UserId = user.Id,
                Email = user.Email!,
                AuthContext = new AuthExecutionContext { IpAddress = ipAddress },
            });

            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.PasswordResetRequest,
                Category: AuditCategories.Authentication,
                Success: true,
                UserId: user.Id,
                IpAddress: ipAddress,
                Metadata: new { email = request.Email }
            ), ct);
        }

        return Results.Ok(new { message = successMessage });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/verify
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> VerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Results.BadRequest(new AuthError("invalid_request", "token is required."));

        if (string.IsNullOrWhiteSpace(request.Type))
            return Results.BadRequest(new AuthError("invalid_request", "type is required (recovery|signup|email_change)."));

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        return request.Type.ToLowerInvariant() switch
        {
            "recovery"     => await HandleRecoveryVerifyAsync(
                                  request, userManager, tokenService, eventCoordinator,
                                  auditLogger, ipAddress, ct),
            "signup"       => await HandleSignupVerifyAsync(
                                  request, userManager, auditLogger, ipAddress, ct),
            "email_change" => await HandleEmailChangeVerifyAsync(
                                  request, userManager, auditLogger, ipAddress, ct),
            _              => Results.BadRequest(new AuthError(
                                  "invalid_request",
                                  $"Unknown type '{request.Type}'. Expected 'recovery', 'signup', or 'email_change'."))
        };
    }

    private static async Task<IResult> HandleRecoveryVerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        IAuditLogger auditLogger,
        string? ipAddress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new AuthError("invalid_request", "email is required for type=recovery."));

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return Results.BadRequest(new AuthError("invalid_request", "new_password is required for type=recovery."));

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Results.BadRequest(new AuthError("invalid_grant", "Invalid or expired reset token."));

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new AuthError(
                "invalid_grant",
                string.Join("; ", result.Errors.Select(e => e.Description))));
        }

        await userManager.UpdateSecurityStampAsync(user);
        await tokenService.RevokeAllForUserAsync(user.Id, ct);

        eventCoordinator.Emit(new PasswordChangedEvent
        {
            UserId = user.Id,
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress },
        });

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.PasswordReset,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: ipAddress,
            Metadata: new { email = request.Email }
        ), ct);

        return Results.Ok(new { message = "Password has been reset successfully." });
    }

    private static async Task<IResult> HandleSignupVerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        string? ipAddress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new AuthError("invalid_request", "email is required for type=signup."));

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Results.BadRequest(new AuthError("invalid_grant", "Invalid confirmation token."));

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new AuthError(
                "invalid_grant",
                string.Join("; ", result.Errors.Select(e => e.Description))));
        }

        user.EmailConfirmedAt = DateTime.UtcNow;
        user.Updated = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.EmailConfirm,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: ipAddress,
            Metadata: new { email = request.Email }
        ), ct);

        return Results.Ok(new { message = "Email confirmed successfully." });
    }

    private static async Task<IResult> HandleEmailChangeVerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        string? ipAddress,
        CancellationToken ct)
    {
        return await HandleSignupVerifyAsync(request, userManager, auditLogger, ipAddress, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/resend
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ResendAsync(
        ResendRequest request,
        UserManager<ApplicationUser> userManager,
        IHPDAuthEmailSender emailSender,
        IAuditLogger auditLogger,
        IMemoryCache cache,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.Ok(new { message = "If your email is registered, a verification email has been sent." });

        var cacheKey = $"resend:{request.Type}:{request.Email.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out _))
            return Results.StatusCode(429);

        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is not null && !user.EmailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), token, ct);

            cache.Set(cacheKey, true, TimeSpan.FromMinutes(ResendCooldownMinutes));

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.EmailConfirmResend,
                Category: AuditCategories.Authentication,
                Success: true,
                UserId: user.Id,
                IpAddress: ipAddress,
                Metadata: new { email = request.Email, type = request.Type }
            ), ct);
        }

        return Results.Ok(new { message = "If your email is registered, a verification email has been sent." });
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>POST /api/auth/recover request body.</summary>
public record RecoverRequest(string Email);

/// <summary>POST /api/auth/verify request body.</summary>
public record VerifyRequest(
    string Token,
    string Type,
    string? Email = null,
    string? NewPassword = null
);

/// <summary>POST /api/auth/resend request body.</summary>
public record ResendRequest(
    string Email,
    string Type = "signup"
);
