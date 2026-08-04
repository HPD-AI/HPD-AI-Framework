using HPD.Auth.Core.Entities;
using HPD.Auth.ControlPlane;
using HPD.Auth.Core.Audit;
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
    public static void Map(RouteGroupBuilder root, IEndpointRouteBuilder app)
    {
        var group = root.MapGroup("/users");

        group.MapGet("/{id}/logins", GetLoginsAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.CredentialsRead)
             .WithName("AdminGetUserLogins")
             .WithSummary("List all external OAuth logins linked to a user.");

        group.MapDelete("/{id}/logins/{provider}", RemoveLoginAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.CredentialsWrite)
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
        IAuthAuditWriter auditWriter,
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

        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.LoginRemove, user.Id, cancellationToken: ct);

        return Results.Ok(new { message = $"External login '{provider}' removed." });
    }
}
