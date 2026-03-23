using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin claims management endpoints.
///
/// Routes registered:
///   GET    /api/admin/users/{id}/claims
///   POST   /api/admin/users/{id}/claims
///   DELETE /api/admin/users/{id}/claims
///   PUT    /api/admin/users/{id}/claims
/// </summary>
public static class AdminUserClaimsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapGet("/{id}/claims", GetClaimsAsync)
             .WithName("AdminGetUserClaims")
             .WithSummary("List all claims on a user.");

        group.MapPost("/{id}/claims", AddClaimAsync)
             .WithName("AdminAddUserClaim")
             .WithSummary("Add a claim to a user.");

        group.MapDelete("/{id}/claims", RemoveClaimAsync)
             .WithName("AdminRemoveUserClaim")
             .WithSummary("Remove a specific claim from a user. Body must identify the claim by type and value.");

        group.MapPut("/{id}/claims", ReplaceClaimAsync)
             .WithName("AdminReplaceUserClaim")
             .WithSummary("Replace an existing claim with a new claim value.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetClaimsAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var claims = await userManager.GetClaimsAsync(user);
        var response = claims.Select(c => new { type = c.Type, value = c.Value });
        return Results.Ok(response);
    }

    private static async Task<IResult> AddClaimAsync(
        string id,
        ClaimDto request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.AddClaimAsync(user, new Claim(request.Type, request.Value));
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserUpdate,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "add_claim", claimType = request.Type }
        ), ct);

        return Results.Ok(new { message = "Claim added." });
    }

    private static async Task<IResult> RemoveClaimAsync(
        string id,
        [Microsoft.AspNetCore.Mvc.FromBody] ClaimDto request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.RemoveClaimAsync(user, new Claim(request.Type, request.Value));
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserUpdate,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "remove_claim", claimType = request.Type }
        ), ct);

        return Results.Ok(new { message = "Claim removed." });
    }

    private static async Task<IResult> ReplaceClaimAsync(
        string id,
        ReplaceClaimDto request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var oldClaim = new Claim(request.Old.Type, request.Old.Value);
        var newClaim = new Claim(request.New.Type, request.New.Value);

        var result = await userManager.ReplaceClaimAsync(user, oldClaim, newClaim);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserUpdate,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new
            {
                adminAction = "replace_claim",
                oldClaimType = request.Old.Type,
                newClaimType = request.New.Type
            }
        ), ct);

        return Results.Ok(new { message = "Claim replaced." });
    }
}

/// <summary>A single claim identified by type and value.</summary>
internal record ClaimDto(string Type, string Value);

/// <summary>Request body for PUT /api/admin/users/{id}/claims (replace).</summary>
internal record ReplaceClaimDto(ClaimDto Old, ClaimDto New);
