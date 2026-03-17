using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.TwoFactor.Services;
using HPD.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace HPD.Auth.TwoFactor.Endpoints;

/// <summary>
/// Minimal API endpoints for TOTP MFA factor management, following a -style
/// factors resource. All routes require the caller to be authenticated.
///
/// Routes registered:
///   GET    /api/auth/factors
///   POST   /api/auth/factors
///   POST   /api/auth/factors/{factorId}/challenge
///   POST   /api/auth/factors/{factorId}/verify
///   DELETE /api/auth/factors/{factorId}
/// </summary>
public static class TotpEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/factors")
                       .RequireAuthorization();

        group.MapGet("/", ListFactorsAsync)
             .WithName("ListMfaFactors")
             .WithSummary("List all enrolled MFA factors (TOTP and passkeys) for the current user.");

        group.MapPost("/", SetupTotpAsync)
             .WithName("SetupTotpFactor")
             .WithSummary("Begin TOTP enrollment: resets the authenticator key and returns the shared key and otpauth URI.");

        group.MapPost("/{factorId}/challenge", ChallengeTotpAsync)
             .WithName("ChallengeTotpFactor")
             .WithSummary("Issue a challenge nonce for the given TOTP factor (implicit challenge — returns a new nonce).");

        group.MapPost("/{factorId}/verify", VerifyTotpAsync)
             .WithName("VerifyTotpFactor")
             .WithSummary("Verify a TOTP code and, on first enrollment, enable 2FA and return one-time recovery codes.");

        group.MapDelete("/{factorId}", DeleteFactorAsync)
             .WithName("DeleteMfaFactor")
             .WithSummary("Remove an enrolled MFA factor. For TOTP, resets the authenticator key and disables 2FA.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/auth/factors
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListFactorsAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        var factors = new List<object>();

        var authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        var is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);

        if (!string.IsNullOrEmpty(authenticatorKey))
        {
            factors.Add(new
            {
                id = $"totp:{user.Id}",
                type = "totp",
                friendlyName = "Authenticator App",
                isEnabled = is2faEnabled,
                recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user),
                createdAt = (DateTime?)null
            });
        }

        var passkeys = await userManager.GetPasskeysAsync(user);
        foreach (var pk in passkeys)
        {
            factors.Add(new
            {
                id = $"passkey:{Convert.ToBase64String(pk.CredentialId)}",
                type = "passkey",
                friendlyName = pk.Name ?? "Security Key",
                createdAt = (DateTime?)pk.CreatedAt.UtcDateTime
            });
        }

        return Results.Ok(factors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/factors
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> SetupTotpAsync(
        SetupTotpRequest? request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        TwoFactorService twoFactorService,
        IOptions<HPDAuthOptions> options,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        await userManager.ResetAuthenticatorKeyAsync(user);

        var unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
            return Results.Problem("Failed to generate authenticator key.", statusCode: 500);

        var email = await userManager.GetEmailAsync(user);
        if (string.IsNullOrEmpty(email))
            return Results.Problem("User has no email address.", statusCode: 500);

        var issuer = options.Value.AppName;
        var sharedKey = twoFactorService.FormatAuthenticatorKey(unformattedKey);
        var authenticatorUri = twoFactorService.GenerateAuthenticatorUri(issuer, email, unformattedKey);

        return Results.Ok(new
        {
            id = $"totp:{user.Id}",
            type = "totp",
            friendlyName = request?.FriendlyName ?? "Authenticator App",
            sharedKey,
            authenticatorUri
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/factors/{factorId}/challenge
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ChallengeTotpAsync(
        string factorId,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        if (!factorId.StartsWith("totp:", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new { error = "factor_not_found", message = "Factor not found or not a TOTP factor." });

        var authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(authenticatorKey))
            return Results.NotFound(new { error = "factor_not_found", message = "TOTP factor has not been set up." });

        var challengeId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(5);

        return Results.Ok(new
        {
            id = challengeId,
            factorId,
            expiresAt
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/factors/{factorId}/verify
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> VerifyTotpAsync(
        string factorId,
        VerifyTotpRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        IEventCoordinator eventCoordinator,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        if (!factorId.StartsWith("totp:", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new { error = "factor_not_found", message = "Factor not found or not a TOTP factor." });

        var factorUserId = factorId["totp:".Length..];
        if (!string.Equals(factorUserId, user.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new { error = "factor_not_found", message = "Factor not found." });

        var authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(authenticatorKey))
            return Results.NotFound(new { error = "factor_not_found", message = "No TOTP setup is in progress. Call POST /api/auth/factors first." });

        var code = request.Code?.Replace(" ", string.Empty)
                                .Replace("-", string.Empty)
                                .Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(code))
            return Results.BadRequest(new { error = "invalid_code", message = "A TOTP code is required." });

        var isValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        if (!isValid)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.TwoFactorVerifyFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                UserId: user.Id,
                IpAddress: ipAddress,
                Metadata: new { factorId, reason = "invalid_code" }
            ), ct);

            return Results.BadRequest(new { error = "invalid_code", message = "Invalid verification code." });
        }

        var wasAlreadyEnabled = await userManager.GetTwoFactorEnabledAsync(user);

        await userManager.SetTwoFactorEnabledAsync(user, true);

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        if (user.RequiredActions.Contains("CONFIGURE_2FA"))
        {
            user.RequiredActions = user.RequiredActions.Where(a => a != "CONFIGURE_2FA").ToList();
            await userManager.UpdateAsync(user);
        }

        eventCoordinator.Emit(new TwoFactorEnabledEvent
        {
            UserId = user.Id,
            Method = "totp",
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress },
        });

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.TwoFactorEnable,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: ipAddress,
            Metadata: new { factorId, firstEnrollment = !wasAlreadyEnabled }
        ), ct);

        return Results.Ok(new
        {
            success = true,
            recoveryCodes
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/auth/factors/{factorId}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> DeleteFactorAsync(
        string factorId,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        IEventCoordinator eventCoordinator,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        if (factorId.StartsWith("totp:", StringComparison.OrdinalIgnoreCase))
        {
            return await DeleteTotpFactorAsync(user, httpContext, userManager, auditLogger, eventCoordinator, ct);
        }

        if (factorId.StartsWith("passkey:", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound(new
            {
                error = "wrong_endpoint",
                message = "Passkey factors must be deleted via DELETE /api/auth/passkeys/{id}."
            });
        }

        return Results.NotFound(new { error = "factor_not_found", message = "Factor not found." });
    }

    private static async Task<IResult> DeleteTotpFactorAsync(
        ApplicationUser user,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        IEventCoordinator eventCoordinator,
        CancellationToken ct)
    {
        var hasAuthenticator = !string.IsNullOrEmpty(await userManager.GetAuthenticatorKeyAsync(user));
        if (!hasAuthenticator)
            return Results.NotFound(new { error = "factor_not_found", message = "No TOTP factor is enrolled." });

        var hasPassword = await userManager.HasPasswordAsync(user);
        var passkeys = await userManager.GetPasskeysAsync(user);

        if (!hasPassword && passkeys.Count == 0)
        {
            return Results.BadRequest(new
            {
                error = "last_auth_method",
                message = "Cannot remove the last authentication method. Please add a password or passkey first."
            });
        }

        await userManager.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");

        var disableResult = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disableResult.Succeeded)
            return Results.BadRequest(new { error = "disable_failed", errors = disableResult.Errors });

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        eventCoordinator.Emit(new TwoFactorEnabledEvent
        {
            UserId = user.Id,
            Method = "totp_disabled",
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress },
        });

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.TwoFactorDisable,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: ipAddress,
            Metadata: new { method = "totp" }
        ), ct);

        return Results.NoContent();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────────────────────

internal record SetupTotpRequest(
    string? Type,
    string? FriendlyName
);

internal record VerifyTotpRequest(
    string? Code,
    string? ChallengeId
);
