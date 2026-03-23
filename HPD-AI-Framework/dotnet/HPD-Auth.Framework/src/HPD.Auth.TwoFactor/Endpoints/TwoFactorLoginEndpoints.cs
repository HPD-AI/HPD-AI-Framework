using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.TwoFactor.Endpoints;

/// <summary>
/// Minimal API endpoint for completing a two-factor authentication login that was
/// interrupted by the 2FA challenge.
///
/// This endpoint is called after a primary login returns
/// <c>{ requiresTwoFactor: true }</c> (i.e., after
/// <see cref="SignInManager{TUser}.PasswordSignInAsync"/> returns
/// <see cref="SignInResult.RequiresTwoFactor"/>). ASP.NET Identity stores the
/// pending-2FA user identity in a short-lived cookie
/// (<see cref="IdentityConstants.TwoFactorUserIdScheme"/>) which
/// <see cref="SignInManager{TUser}.GetTwoFactorAuthenticationUserAsync"/> reads.
///
/// Routes registered:
///   POST /api/auth/2fa/verify
/// </summary>
public static class TwoFactorLoginEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/2fa/verify", VerifyTwoFactorLoginAsync)
           .WithName("TwoFactorLoginVerify")
           .WithSummary(
               "Complete a two-factor login. Supply either a TOTP code or a recovery code. " +
               "Returns a token response (JWT) or 200 (cookie mode) on success.")
           // This endpoint is intentionally NOT protected by RequireAuthorization()
           // because the caller is in a partially-authenticated state (they only have
           // the TwoFactorUserId cookie, not a full identity cookie or Bearer token).
           .AllowAnonymous();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/2fa/verify
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies a two-factor code or recovery code and completes the login.
    ///
    /// Flow:
    ///   1. <see cref="SignInManager{TUser}.GetTwoFactorAuthenticationUserAsync"/> retrieves the
    ///      pending user from the TwoFactorUserId session cookie. Returns 400 if no
    ///      pending session is found (e.g., the caller did not attempt primary login first
    ///      or the session expired).
    ///   2. If <see cref="TwoFactorLoginRequest.RecoveryCode"/> is provided, the recovery
    ///      code path is used (<see cref="SignInManager{TUser}.TwoFactorRecoveryCodeSignInAsync"/>).
    ///      Recovery codes are single-use; this call consumes the code.
    ///   3. Otherwise the TOTP path is used (<see cref="SignInManager{TUser}.TwoFactorAuthenticatorSignInAsync"/>).
    ///      <c>isPersistent</c> maps to <see cref="TwoFactorLoginRequest.RememberMe"/>.
    ///      <c>rememberClient</c> maps to <see cref="TwoFactorLoginRequest.RememberDevice"/>
    ///      and, when true, sets the TwoFactorRememberMe cookie so that subsequent logins
    ///      from the same device skip the 2FA prompt.
    ///   4. Returns 423 Locked if the account was locked out during the verification.
    ///   5. Returns 401 Unauthorized if the code is incorrect.
    ///   6. On success, issues a token response via <see cref="ITokenService"/> and includes
    ///      any pending <see cref="ApplicationUser.RequiredActions"/> in the response.
    /// </summary>
    private static async Task<IResult> VerifyTwoFactorLoginAsync(
        TwoFactorLoginRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        // Step 1: Retrieve the pending-2FA user from the session cookie.
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return Results.BadRequest(new
            {
                error = "no_2fa_session",
                message = "No two-factor authentication session found. Please sign in again."
            });
        }

        SignInResult result;

        // Step 2/3: Use recovery code or TOTP code path.
        if (!string.IsNullOrEmpty(request.RecoveryCode))
        {
            // Identity generates recovery codes in "AAAAA-BBBBB" format.  Users may
            // paste them with extra hyphens or spaces.  We normalise by stripping all
            // hyphens and spaces from BOTH the submitted code AND each stored code,
            // then look up the canonical stored form so Identity's own redemption
            // logic (which compares exact strings) still works correctly.
            var submittedNorm = StripFormatting(request.RecoveryCode);
            var canonical = await FindCanonicalRecoveryCodeAsync(user, submittedNorm, userManager);

            result = canonical is not null
                ? await signInManager.TwoFactorRecoveryCodeSignInAsync(canonical)
                : SignInResult.Failed;
        }
        else
        {
            if (string.IsNullOrEmpty(request.Code))
            {
                return Results.BadRequest(new
                {
                    error = "code_required",
                    message = "Either 'code' (TOTP) or 'recoveryCode' must be provided."
                });
            }

            var normalizedTotpCode = request.Code.Replace(" ", string.Empty).Trim();

            result = await signInManager.TwoFactorAuthenticatorSignInAsync(
                normalizedTotpCode,
                isPersistent: request.RememberMe,
                rememberClient: request.RememberDevice);
        }

        // Step 4: Locked out.
        if (result.IsLockedOut)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.AccountLockout,
                Category: AuditCategories.Authentication,
                Success: false,
                UserId: user.Id,
                IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
                Metadata: new { reason = "2fa_lockout" }
            ), ct);

            return Results.StatusCode(423); // 423 Locked
        }

        // Step 5: Code was incorrect.
        if (!result.Succeeded)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.TwoFactorVerifyFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                UserId: user.Id,
                IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
                Metadata: new
                {
                    method = !string.IsNullOrEmpty(request.RecoveryCode) ? "recovery_code" : "totp"
                }
            ), ct);

            return Results.Unauthorized();
        }

        // Step 6: Success — update tracking fields, issue tokens.
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = httpContext.Connection.RemoteIpAddress?.ToString();
        await userManager.UpdateAsync(user);

        var auditMethod = !string.IsNullOrEmpty(request.RecoveryCode) ? "recovery_code" : "totp";

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.TwoFactorVerify,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: user.LastLoginIp,
            Metadata: new
            {
                method = auditMethod,
                rememberDevice = request.RememberDevice
            }
        ), ct);

        var tokens = await tokenService.GenerateTokensAsync(user, ct);

        // Warn the user if their recovery code count is critically low.
        var recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);

        // Always return a consistent camelCase anonymous-object shape so callers
        // always see the same property names regardless of whether warnings or
        // requiredActions are present.  (Returning TokenResponse directly would
        // use snake_case [JsonPropertyName] attributes, which is a different shape.)
        return Results.Ok(new
        {
            accessToken = tokens.AccessToken,
            tokenType = tokens.TokenType,
            expiresIn = tokens.ExpiresIn,
            expiresAt = tokens.ExpiresAt,
            refreshToken = tokens.RefreshToken,
            user = tokens.User,
            requiredActions = user.RequiredActions,
            warnings = BuildWarnings(recoveryCodesLeft)
        });
    }

    private static string StripFormatting(string code) =>
        code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    /// <summary>
    /// Reads the user's stored recovery codes (joined by ";") and finds the one
    /// whose normalised form matches <paramref name="normalizedSubmitted"/>.
    /// Returns the canonical stored code (with its original formatting) so that
    /// <see cref="SignInManager{TUser}.TwoFactorRecoveryCodeSignInAsync"/> can
    /// redeem it by exact-string match.
    /// Returns <see langword="null"/> if no match is found.
    /// </summary>
    private static async Task<string?> FindCanonicalRecoveryCodeAsync(
        ApplicationUser user,
        string normalizedSubmitted,
        UserManager<ApplicationUser> userManager)
    {
        // Identity stores recovery codes as a single ";"-joined token in UserTokens.
        // The public API exposes CountRecoveryCodesAsync but not the raw list.
        // We retrieve them via the authentication token accessor.
        var mergedCodes = await userManager.GetAuthenticationTokenAsync(
            user, "[AspNetUserStore]", "RecoveryCodes");

        if (string.IsNullOrEmpty(mergedCodes))
            return null;

        return mergedCodes
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(c => StripFormatting(c) == normalizedSubmitted);
    }

    private static IEnumerable<string> BuildWarnings(int recoveryCodesLeft)
    {
        if (recoveryCodesLeft == 0)
            yield return "You have no recovery codes left. Generate new ones immediately.";
        else if (recoveryCodesLeft < 3)
            yield return $"You only have {recoveryCodesLeft} recovery code(s) remaining. Consider regenerating them.";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Body for POST /api/auth/2fa/verify.
/// Supply either <see cref="Code"/> (TOTP) or <see cref="RecoveryCode"/>, not both.
/// </summary>
internal record TwoFactorLoginRequest(
    /// <summary>
    /// 6-digit TOTP code from an authenticator app.
    /// Mutually exclusive with <see cref="RecoveryCode"/>.
    /// </summary>
    string? Code,

    /// <summary>
    /// Single-use recovery code (e.g., "A1B2-C3D4-E5F6").
    /// Formatting characters (spaces, hyphens) are stripped automatically.
    /// Mutually exclusive with <see cref="Code"/>.
    /// </summary>
    string? RecoveryCode,

    /// <summary>
    /// When true, the primary authentication cookie is set as a persistent (session-surviving)
    /// cookie. Maps to the <c>isPersistent</c> parameter of
    /// <see cref="SignInManager{TUser}.TwoFactorAuthenticatorSignInAsync"/>.
    /// Ignored when using a recovery code.
    /// </summary>
    bool RememberMe = false,

    /// <summary>
    /// When true, sets the TwoFactorRememberMe cookie so that the current device is
    /// trusted for future logins and the 2FA prompt is bypassed.
    /// Maps to the <c>rememberClient</c> parameter of
    /// <see cref="SignInManager{TUser}.TwoFactorAuthenticatorSignInAsync"/>.
    /// Ignored when using a recovery code.
    /// </summary>
    bool RememberDevice = false
);
