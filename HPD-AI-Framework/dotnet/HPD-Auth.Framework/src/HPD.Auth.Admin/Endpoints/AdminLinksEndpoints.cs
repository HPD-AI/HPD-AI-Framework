using HPD.Auth.Admin.Models;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin link generation endpoints ( generate_link equivalent).
///
/// Routes registered:
///   POST /api/admin/generate-link
/// </summary>
public static class AdminLinksEndpoints
{
    /// <summary>
    /// Supported link type values that map to UserManager token generators.
    /// </summary>
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "signup",
        "invite",
        "magiclink",
        "recovery",
        "verify_email"
    };

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/generate-link", GenerateLinkAsync)
           .RequireAuthorization("RequireAdmin")
           .WithName("AdminGenerateLink")
           .WithSummary(
               "Generate an action link for a user. " +
               "Supported types: signup, invite, magiclink, recovery, verify_email.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handler
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GenerateLinkAsync(
        AdminGenerateLinkRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        if (!SupportedTypes.Contains(request.Type))
        {
            return Results.BadRequest(new
            {
                error = $"Unsupported link type '{request.Type}'. " +
                        $"Valid types: {string.Join(", ", SupportedTypes)}"
            });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Results.NotFound(new { error = $"No user found with email '{request.Email}'." });

        // ── Generate the appropriate token based on type ──────────────────────
        string token;
        switch (request.Type.ToLowerInvariant())
        {
            case "signup":
            case "invite":
            case "verify_email":
                // Email confirmation token — confirms/activates the account.
                token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                break;

            case "magiclink":
                // Generate a magic-link / passwordless sign-in token.
                // We use a user-token with a "MagicLink" purpose so it is
                // validated by the auth endpoint (not a standard Identity flow).
                token = await userManager.GenerateUserTokenAsync(
                    user,
                    TokenOptions.DefaultEmailProvider,
                    "MagicLink");
                break;

            case "recovery":
                // Password-reset token — user clicks link, lands on reset page.
                token = await userManager.GeneratePasswordResetTokenAsync(user);
                break;

            default:
                // Should never reach here due to the guard above, but keeps
                // the compiler happy and serves as a defensive catch-all.
                return Results.BadRequest(new { error = "Unsupported type." });
        }

        // ── Build the action link ─────────────────────────────────────────────
        string redirectBase = request.RedirectTo ?? string.Empty;
        string separator = redirectBase.Contains('?') ? "&" : "?";
        string actionLink =
            $"{redirectBase}{separator}token={Uri.EscapeDataString(token)}" +
            $"&userId={user.Id}" +
            $"&type={request.Type.ToLowerInvariant()}";

        // ── Hash the token for the audit record (do NOT store the raw token) ──
        string hashedToken = HashToken(token);

        // ── Audit — log the action but NOT the raw token value ───────────────
        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserUpdate,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new
            {
                adminAction = "generate_link",
                linkType = request.Type,
                hashedToken,
                redirectTo = request.RedirectTo
            }
        ), ct);

        return Results.Ok(new AdminGenerateLinkResponse(
            ActionLink: actionLink,
            HashedToken: hashedToken,
            VerificationType: request.Type.ToLowerInvariant(),
            RedirectTo: redirectBase
        ));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute a hex-encoded SHA-256 hash of the raw token for audit records.
    /// The raw token itself is never stored — only the hash.
    /// </summary>
    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
