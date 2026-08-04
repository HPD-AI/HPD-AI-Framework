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
/// Admin role management endpoints.
///
/// Routes registered:
///   GET    /api/admin/users/{id}/roles
///   POST   /api/admin/users/{id}/roles
///   DELETE /api/admin/users/{id}/roles/{role}
/// </summary>
public static class AdminUserRolesEndpoints
{
    public static void Map(RouteGroupBuilder root, IEndpointRouteBuilder app)
    {
        var group = root.MapGroup("/users");

        group.MapGet("/{id}/roles", GetRolesAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.AuthorizationRead)
             .WithName("AdminGetUserRoles")
             .WithSummary("List all roles assigned to a user.");

        group.MapPost("/{id}/roles", AddRoleAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.AuthorizationWrite)
             .WithName("AdminAddUserRole")
             .WithSummary("Assign a role to a user.");

        group.MapDelete("/{id}/roles/{role}", RemoveRoleAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.AuthorizationWrite)
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
        IAuthAuditWriter auditWriter,
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

        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.RoleAdd, user.Id, cancellationToken: ct);

        return Results.Ok(new { message = $"Role '{request.Role}' assigned." });
    }

    private static async Task<IResult> RemoveRoleAsync(
        string id,
        string role,
        UserManager<ApplicationUser> userManager,
        IAuthAuditWriter auditWriter,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.RoleRemove, user.Id, cancellationToken: ct);

        return Results.Ok(new { message = $"Role '{role}' removed." });
    }
}

/// <summary>Request body for POST /api/admin/users/{id}/roles.</summary>
internal record RoleRequest(string Role);
