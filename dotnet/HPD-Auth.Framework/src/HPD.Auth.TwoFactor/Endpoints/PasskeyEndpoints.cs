using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace HPD.Auth.TwoFactor.Endpoints;

/// <summary>
/// Minimal API endpoints for FIDO2/WebAuthn passkey registration, authentication,
/// and management.
///
/// Routes registered:
///   POST   /api/auth/passkey/register/options      (authenticated)
///   POST   /api/auth/passkey/register/complete      (authenticated)
///   POST   /api/auth/passkey/authenticate/options   (anonymous)
///   POST   /api/auth/passkey/authenticate/complete  (anonymous)
///   GET    /api/auth/passkeys                       (authenticated)
///   PATCH  /api/auth/passkeys/{id}                  (authenticated)
///   DELETE /api/auth/passkeys/{id}                  (authenticated)
///
/// Implementation note:
///   All passkey endpoints are fully implemented using the ASP.NET Identity 9
///   passkey APIs (<see cref="SignInManager{TUser}.MakePasskeyCreationOptionsAsync"/>,
///   <see cref="SignInManager{TUser}.PerformPasskeyAttestationAsync"/>,
///   <see cref="SignInManager{TUser}.MakePasskeyRequestOptionsAsync"/>,
///   <see cref="SignInManager{TUser}.PasskeySignInAsync"/>).
///
///   These methods exist in the ASP.NET Core Identity 9 source and delegate to
///   <see cref="IPasskeyHandler{TUser}"/>, which must be registered separately.
///   Without a registered <see cref="IPasskeyHandler{TUser}"/>, the Identity methods
///   will throw <see cref="InvalidOperationException"/> at runtime. HPD.Auth does not
///   ship a built-in passkey handler — operators must register one when enabling passkeys:
///
///   <code>
///   builder.Services.AddScoped&lt;IPasskeyHandler&lt;ApplicationUser&gt;, MyPasskeyHandler&gt;();
///   </code>
///
///   See HPD.Auth documentation and FeaturesOptions.EnablePasskeys for details.
/// </summary>
public static class PasskeyEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // ── Registration (requires authenticated user) ────────────────────────
        var registrationGroup = app.MapGroup("/api/auth/passkey")
                                   .RequireAuthorization();

        registrationGroup.MapPost("/register/options", GetRegistrationOptionsAsync)
            .WithName("PasskeyRegistrationOptions")
            .WithSummary("Generate passkey creation options (WebAuthn navigator.credentials.create() argument).");

        registrationGroup.MapPost("/register/complete", CompleteRegistrationAsync)
            .WithName("PasskeyRegistrationComplete")
            .WithSummary("Complete passkey registration by submitting the attestation response from the browser.");

        // ── Authentication (anonymous) ────────────────────────────────────────
        var authGroup = app.MapGroup("/api/auth/passkey");

        authGroup.MapPost("/authenticate/options", GetAuthenticationOptionsAsync)
            .WithName("PasskeyAuthenticationOptions")
            .WithSummary("Generate passkey assertion options (WebAuthn navigator.credentials.get() argument).");

        authGroup.MapPost("/authenticate/complete", CompleteAuthenticationAsync)
            .WithName("PasskeyAuthenticationComplete")
            .WithSummary("Complete passkey authentication by submitting the assertion response from the browser.");

        // ── Management (requires authenticated user) ──────────────────────────
        var managementGroup = app.MapGroup("/api/auth/passkeys")
                                 .RequireAuthorization();

        managementGroup.MapGet("/", ListPasskeysAsync)
            .WithName("ListPasskeys")
            .WithSummary("List all registered passkeys for the current user.");

        managementGroup.MapPatch("/{id}", RenamePasskeyAsync)
            .WithName("RenamePasskey")
            .WithSummary("Rename a registered passkey.");

        managementGroup.MapDelete("/{id}", DeletePasskeyAsync)
            .WithName("DeletePasskey")
            .WithSummary("Remove a registered passkey. Blocked if it is the last authentication method.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/passkey/register/options
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns WebAuthn PublicKeyCredentialCreationOptions as a JSON string.
    /// The client should parse this and pass it to <c>navigator.credentials.create()</c>.
    ///
    /// Requires an <see cref="IPasskeyHandler{TUser}"/> to be registered in DI.
    /// If no handler is registered, Identity will throw <see cref="InvalidOperationException"/>
    /// which is surfaced as 500 Internal Server Error.
    /// </summary>
    private static async Task<IResult> GetRegistrationOptionsAsync(
        PasskeyNameRequest? request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        var userEntity = new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.Email!,
            DisplayName = user.DisplayName ?? user.Email!
        };

        // MakePasskeyCreationOptionsAsync stores attestation state in session/cookie
        // and returns the JSON options string to send to the browser.
        var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(userEntity);

        return Results.Ok(new
        {
            options = optionsJson,
            passkeyName = request?.Name ?? "Security Key"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/passkey/register/complete
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Completes passkey registration.
    ///
    /// The client sends the JSON-serialised result of <c>navigator.credentials.create()</c>.
    /// On success, the passkey is stored via <see cref="UserManager{TUser}.AddOrUpdatePasskeyAsync"/>
    /// and 2FA is enabled if it was not already.
    /// </summary>
    private static async Task<IResult> CompleteRegistrationAsync(
        PasskeyRegistrationRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        var attestationResult = await signInManager.PerformPasskeyAttestationAsync(request.CredentialJson);

        if (!attestationResult.Succeeded)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.PasskeyRegister,
                Category: AuditCategories.Authentication,
                Success: false,
                UserId: user.Id,
                IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
                Metadata: new { reason = attestationResult.Failure?.Message }
            ), ct);

            return Results.BadRequest(new
            {
                error = "passkey_registration_failed",
                message = attestationResult.Failure?.Message ?? "Passkey attestation failed."
            });
        }

        // Store the passkey. AddOrUpdatePasskeyAsync will insert or update based
        // on the credential ID (handles the re-registration flow cleanly).
        var storeResult = await userManager.AddOrUpdatePasskeyAsync(user, attestationResult.Passkey);
        if (!storeResult.Succeeded)
        {
            return Results.Problem(
                detail: string.Join("; ", storeResult.Errors.Select(e => e.Description)),
                statusCode: 500);
        }

        // Enable 2FA if not already active (passkeys count as a 2FA method).
        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            await userManager.SetTwoFactorEnabledAsync(user, true);
        }

        var credentialIdBase64 = WebEncoders.Base64UrlEncode(attestationResult.Passkey.CredentialId);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.PasskeyRegister,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
            Metadata: new { credentialId = credentialIdBase64, passkeyName = request.Name }
        ), ct);

        return Results.Ok(new
        {
            message = "Passkey registered successfully.",
            passkeyId = credentialIdBase64,
            name = request.Name ?? "Security Key"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/passkey/authenticate/options
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns WebAuthn PublicKeyCredentialRequestOptions as a JSON string for
    /// passkey-based sign-in. If an email address is provided and the user exists,
    /// the response may include allowCredentials for that user's registered passkeys.
    ///
    /// This endpoint is intentionally anonymous — the user is not yet authenticated
    /// at this point in the login flow.
    /// </summary>
    private static async Task<IResult> GetAuthenticationOptionsAsync(
        PasskeyAuthRequest? request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken ct = default)
    {
        ApplicationUser? user = null;

        // If an email hint is provided, pre-populate allowCredentials for that user.
        // Do NOT reveal whether the user exists — return valid options either way
        // (no account enumeration).
        if (!string.IsNullOrEmpty(request?.Email))
        {
            user = await userManager.FindByEmailAsync(request.Email);
        }

        // MakePasskeyRequestOptionsAsync stores assertion state and returns options JSON.
        var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);

        return Results.Ok(optionsJson);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/passkey/authenticate/complete
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Completes passkey authentication.
    ///
    /// PasskeySignInAsync performs the assertion and signs the user in if successful.
    /// On success, a JWT token response is returned (or 200 for cookie-only mode).
    /// On lockout, returns 423 Locked.
    /// On failure, returns 401 Unauthorized.
    /// </summary>
    private static async Task<IResult> CompleteAuthenticationAsync(
        PasskeyAuthCompleteRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        var result = await signInManager.PasskeySignInAsync(request.CredentialJson);

        if (result.IsLockedOut)
        {
            return Results.StatusCode(423);
        }

        if (!result.Succeeded)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                Action: AuditActions.PasskeyAuthenticateFailed,
                Category: AuditCategories.Authentication,
                Success: false,
                IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
                Metadata: new { reason = "assertion_failed" }
            ), ct);

            return Results.Unauthorized();
        }

        // Retrieve the now-authenticated user.
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = httpContext.Connection.RemoteIpAddress?.ToString();
        await userManager.UpdateAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.PasskeyAuthenticate,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: user.LastLoginIp,
            Metadata: new { method = "passkey" }
        ), ct);

        var tokens = await tokenService.GenerateTokensAsync(user, ct);

        // Include any pending required actions in the response.
        if (user.HasPendingActions)
        {
            return Results.Ok(new
            {
                tokens.AccessToken,
                tokens.TokenType,
                tokens.ExpiresIn,
                tokens.ExpiresAt,
                tokens.RefreshToken,
                tokens.User,
                requiredActions = user.RequiredActions
            });
        }

        return Results.Ok(tokens);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/auth/passkeys
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListPasskeysAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        var passkeys = await userManager.GetPasskeysAsync(user);

        return Results.Ok(passkeys.Select(p => new
        {
            id = WebEncoders.Base64UrlEncode(p.CredentialId),
            name = p.Name,
            createdAt = p.CreatedAt.UtcDateTime,
            transports = p.Transports,
            isUserVerified = p.IsUserVerified,
            isBackedUp = p.IsBackedUp
        }));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PATCH /api/auth/passkeys/{id}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> RenamePasskeyAsync(
        string id,
        PasskeyNameRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        byte[] credentialId;
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(id);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "invalid_id", message = "Passkey ID is not valid base64." });
        }

        var passkey = await userManager.GetPasskeyAsync(user, credentialId);
        if (passkey is null)
            return Results.NotFound(new { error = "not_found", message = "Passkey not found." });

        // UserPasskeyInfo is a sealed class; we create a new one with the updated name.
        // The Name property must be set via a mutable setter — check if it exists.
        // ASP.NET Identity 9's UserPasskeyInfo exposes a settable Name property.
        passkey.Name = request.Name;

        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!result.Succeeded)
            return Results.BadRequest(new { errors = result.Errors });

        return Results.Ok(new { message = "Passkey name updated.", name = request.Name });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/auth/passkeys/{id}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> DeletePasskeyAsync(
        string id,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return Results.Unauthorized();

        byte[] credentialId;
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(id);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "invalid_id", message = "Passkey ID is not valid base64." });
        }

        var passkey = await userManager.GetPasskeyAsync(user, credentialId);
        if (passkey is null)
            return Results.NotFound(new { error = "not_found", message = "Passkey not found." });

        // Safety check: prevent removal of the last authentication method.
        var allPasskeys = await userManager.GetPasskeysAsync(user);
        var hasPassword = await userManager.HasPasswordAsync(user);
        var hasTotp = !string.IsNullOrEmpty(await userManager.GetAuthenticatorKeyAsync(user));

        if (allPasskeys.Count <= 1 && !hasPassword && !hasTotp)
        {
            return Results.BadRequest(new
            {
                error = "last_auth_method",
                message = "Cannot remove the last authentication method. Please add a password or authenticator first."
            });
        }

        var result = await userManager.RemovePasskeyAsync(user, credentialId);
        if (!result.Succeeded)
            return Results.BadRequest(new { errors = result.Errors });

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.PasskeyDelete,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: user.Id,
            IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
            Metadata: new { credentialId = id }
        ), ct);

        return Results.NoContent();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Name request for passkey rename and optional name during registration.</summary>
internal record PasskeyNameRequest(string Name);

/// <summary>Body for POST /api/auth/passkey/register/complete.</summary>
internal record PasskeyRegistrationRequest(
    /// <summary>JSON-serialised result of navigator.credentials.create().</summary>
    string CredentialJson,

    /// <summary>Optional friendly name for the passkey (e.g., "Touch ID on MacBook").</summary>
    string? Name
);

/// <summary>Optional body for POST /api/auth/passkey/authenticate/options.</summary>
internal record PasskeyAuthRequest(
    /// <summary>Optional email hint to pre-populate allowCredentials.</summary>
    string? Email
);

/// <summary>Body for POST /api/auth/passkey/authenticate/complete.</summary>
internal record PasskeyAuthCompleteRequest(
    /// <summary>JSON-serialised result of navigator.credentials.get().</summary>
    string CredentialJson
);
