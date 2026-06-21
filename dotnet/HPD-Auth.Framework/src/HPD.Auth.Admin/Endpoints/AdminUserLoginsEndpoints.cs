using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin external login management endpoints.
///
/// Routes registered:
///   GET    /api/admin/users/{id}/logins
///   DELETE /api/admin/users/{id}/logins/{provider}?providerKey={key}
/// </summary>
public static class AdminUserLoginsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapGet("/{id}/logins", GetLoginsAsync)
             .WithName("AdminGetUserLogins")
             .WithSummary("List all external OAuth logins linked to a user.");

        group.MapDelete("/{id}/logins/{provider}", RemoveLoginAsync)
             .WithName("AdminRemoveUserLogin")
             .WithSummary("Remove an external OAuth login from a user. Requires providerKey query param.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetLoginsAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var logins = await userManager.GetLoginsAsync(user);
        var response = logins.Select(l => new
        {
            provider = l.LoginProvider,
            providerKey = l.ProviderKey,
            displayName = l.ProviderDisplayName
        });

        return Results.Ok(response);
    }

    private static async Task<IResult> RemoveLoginAsync(
        string id,
        string provider,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        string? providerKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            return Results.BadRequest(new { error = "providerKey query parameter is required." });

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.RemoveLoginAsync(user, provider, providerKey);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.OAuthUnlink,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "remove_login", provider }
        ), ct);

        return Results.Ok(new { message = $"External login '{provider}' removed." });
    }
}
