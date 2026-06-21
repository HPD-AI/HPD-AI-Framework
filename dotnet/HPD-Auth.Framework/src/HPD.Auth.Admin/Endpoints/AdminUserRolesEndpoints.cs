using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin role management endpoints.
///
/// Routes registered:
///   GET    /api/admin/users/{id}/roles
///   POST   /api/admin/users/{id}/roles
///   DELETE /api/admin/users/{id}/roles/{role}
/// </summary>
public static class AdminUserRolesEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapGet("/{id}/roles", GetRolesAsync)
             .WithName("AdminGetUserRoles")
             .WithSummary("List all roles assigned to a user.");

        group.MapPost("/{id}/roles", AddRoleAsync)
             .WithName("AdminAddUserRole")
             .WithSummary("Assign a role to a user.");

        group.MapDelete("/{id}/roles/{role}", RemoveRoleAsync)
             .WithName("AdminRemoveUserRole")
             .WithSummary("Remove a role from a user.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetRolesAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(roles);
    }

    private static async Task<IResult> AddRoleAsync(
        string id,
        RoleRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        IdentityResult result;
        try
        {
            result = await userManager.AddToRoleAsync(user, request.Role);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminRoleAssign,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "add_role", role = request.Role }
        ), ct);

        return Results.Ok(new { message = $"Role '{request.Role}' assigned." });
    }

    private static async Task<IResult> RemoveRoleAsync(
        string id,
        string role,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminRoleRemove,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "remove_role", role }
        ), ct);

        return Results.Ok(new { message = $"Role '{role}' removed." });
    }
}

/// <summary>Request body for POST /api/admin/users/{id}/roles.</summary>
internal record RoleRequest(string Role);
