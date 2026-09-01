using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Serialization;
using HPD.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// Maps password recovery, OTP verification, and resend endpoints.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/recover", RecoverRequestDelegate)
             .WithName("AuthRecover")
             .WithSummary("Request a password reset email. Always returns 200 to prevent email enumeration.");

        group.MapPost("/verify", VerifyRequestDelegate)
             .WithName("AuthVerify")
             .WithSummary("Verify an OTP token. type=recovery resets password; type=signup confirms email.");

        group.MapPost("/resend", ResendRequestDelegate)
             .WithName("AuthResend")
             .WithSummary("Resend a verification/confirmation email. Rate-limited to one request per 5 minutes.");
    }

    private static async Task RecoverRequestDelegate(HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        RecoverRequest? request;
        try
        {
            request = await AuthEndpointJson.ReadJsonAsync(
                httpContext,
                HPDAuthJsonSerializerContext.Default.RecoverRequest,
                ct);
        }
        catch
        {
            await AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Malformed request body."))
                .ExecuteAsync(httpContext);
            return;
        }

        if (request is null)
        {
            await AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Request body is required."))
                .ExecuteAsync(httpContext);
            return;
        }

        var services = httpContext.RequestServices;
        var result = await RecoverAsync(
            request,
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<IHPDAuthEmailSender>(),
            services.GetRequiredService<IEventCoordinator>(),
            httpContext,
            ct);

        await result.ExecuteAsync(httpContext);
    }

    private static async Task VerifyRequestDelegate(HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        VerifyRequest? request;
        try
        {
            request = await AuthEndpointJson.ReadJsonAsync(
                httpContext,
                HPDAuthJsonSerializerContext.Default.VerifyRequest,
                ct);
        }
        catch
        {
            await AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Malformed request body."))
                .ExecuteAsync(httpContext);
            return;
        }

        if (request is null)
        {
            await AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Request body is required."))
                .ExecuteAsync(httpContext);
            return;
        }

        var services = httpContext.RequestServices;
        var result = await VerifyAsync(
            request,
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<IAuthPasswordResetCommand>(),
            services.GetRequiredService<ITokenService>(),
            services.GetRequiredService<IEventCoordinator>(),
            httpContext,
            ct);

        await result.ExecuteAsync(httpContext);
    }

    private static async Task ResendRequestDelegate(HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        ResendRequest? request;
        try
        {
            request = await AuthEndpointJson.ReadJsonAsync(
                httpContext,
                HPDAuthJsonSerializerContext.Default.ResendRequest,
                ct);
        }
        catch
        {
            await AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Malformed request body."))
                .ExecuteAsync(httpContext);
            return;
        }

        if (request is null)
        {
            await AuthEndpointJson.BadRequest(new AuthError("invalid_request", "Request body is required."))
                .ExecuteAsync(httpContext);
            return;
        }

        var services = httpContext.RequestServices;
        var result = await ResendAsync(
            request,
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<IHPDAuthEmailSender>(),
            services.GetRequiredService<IMemoryCache>(),
            httpContext,
            ct);

        await result.ExecuteAsync(httpContext);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/recover
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> RecoverAsync(
        RecoverRequest request,
        UserManager<ApplicationUser> userManager,
        IHPDAuthEmailSender emailSender,
        IEventCoordinator eventCoordinator,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        const string successMessage = "If your email is registered, you will receive a reset link.";

        if (string.IsNullOrWhiteSpace(request.Email))
            return AuthEndpointJson.Ok(
                new MessageResponse(successMessage),
                HPDAuthJsonSerializerContext.Default.MessageResponse);

        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendPasswordResetAsync(user.Email!, user.Id.ToString(), token, ct);

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

            await eventCoordinator.EmitAsync(new PasswordResetRequestedEvent
            {
                UserId = user.Id,
                Email = user.Email!,
                AuthContext = new AuthExecutionContext { IpAddress = ipAddress },
            }, ct);

        }

        return AuthEndpointJson.Ok(
            new MessageResponse(successMessage),
            HPDAuthJsonSerializerContext.Default.MessageResponse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/verify
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> VerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        IAuthPasswordResetCommand passwordReset,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "token is required."));

        if (string.IsNullOrWhiteSpace(request.Type))
            return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "type is required (recovery|signup|email_change)."));

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        return request.Type.ToLowerInvariant() switch
        {
            "recovery"     => await HandleRecoveryVerifyAsync(
                                  request, userManager, passwordReset, tokenService, eventCoordinator,
                                  ipAddress, ct),
            "signup"       => await HandleSignupVerifyAsync(
                                  request, userManager, eventCoordinator, ipAddress, ct),
            "email_change" => await HandleEmailChangeVerifyAsync(
                                  request, userManager, eventCoordinator, ipAddress, ct),
            _              => AuthEndpointJson.BadRequest(new AuthError(
                                  "invalid_request",
                                  $"Unknown type '{request.Type}'. Expected 'recovery', 'signup', or 'email_change'."))
        };
    }

    private static async Task<IResult> HandleRecoveryVerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        IAuthPasswordResetCommand passwordReset,
        ITokenService tokenService,
        IEventCoordinator eventCoordinator,
        string? ipAddress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "email is required for type=recovery."));

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "new_password is required for type=recovery."));

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return AuthEndpointJson.BadRequest(new AuthError("invalid_grant", "Invalid or expired reset token."));

        var result = await passwordReset.ResetWithTokenAsync(
            user, request.Token, request.NewPassword, ct);
        if (!result.Succeeded)
        {
            return AuthEndpointJson.BadRequest(new AuthError(
                "invalid_grant",
                string.Join("; ", result.Errors.Select(e => e.Description))));
        }

        await tokenService.RevokeAllForUserAsync(user.Id, ct);

        await eventCoordinator.EmitAsync(new PasswordChangedEvent
        {
            UserId = user.Id,
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress },
        }, ct);

        return AuthEndpointJson.Ok(
            new MessageResponse("Password has been reset successfully."),
            HPDAuthJsonSerializerContext.Default.MessageResponse);
    }

    private static async Task<IResult> HandleSignupVerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        IEventCoordinator eventCoordinator,
        string? ipAddress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return AuthEndpointJson.BadRequest(new AuthError("invalid_request", "email is required for type=signup."));

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return AuthEndpointJson.BadRequest(new AuthError("invalid_grant", "Invalid confirmation token."));

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return AuthEndpointJson.BadRequest(new AuthError(
                "invalid_grant",
                string.Join("; ", result.Errors.Select(e => e.Description))));
        }

        user.EmailConfirmedAt = DateTime.UtcNow;
        user.Updated = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await eventCoordinator.EmitAsync(new EmailConfirmedEvent
        {
            UserId = user.Id,
            Email = user.Email!,
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress }
        }, ct);

        return AuthEndpointJson.Ok(
            new MessageResponse("Email confirmed successfully."),
            HPDAuthJsonSerializerContext.Default.MessageResponse);
    }

    private static async Task<IResult> HandleEmailChangeVerifyAsync(
        VerifyRequest request,
        UserManager<ApplicationUser> userManager,
        IEventCoordinator eventCoordinator,
        string? ipAddress,
        CancellationToken ct)
    {
        return await HandleSignupVerifyAsync(request, userManager, eventCoordinator, ipAddress, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/resend
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ResendAsync(
        ResendRequest request,
        UserManager<ApplicationUser> userManager,
        IHPDAuthEmailSender emailSender,
        IMemoryCache cache,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return AuthEndpointJson.Ok(
                new MessageResponse("If your email is registered, a verification email has been sent."),
                HPDAuthJsonSerializerContext.Default.MessageResponse);

        var cacheKey = $"resend:{request.Type}:{request.Email.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out _))
            return Results.StatusCode(429);

        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is not null && !user.EmailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), token, ct);

            cache.Set(cacheKey, true, TimeSpan.FromMinutes(ResendCooldownMinutes));

        }

        return AuthEndpointJson.Ok(
            new MessageResponse("If your email is registered, a verification email has been sent."),
            HPDAuthJsonSerializerContext.Default.MessageResponse);
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
