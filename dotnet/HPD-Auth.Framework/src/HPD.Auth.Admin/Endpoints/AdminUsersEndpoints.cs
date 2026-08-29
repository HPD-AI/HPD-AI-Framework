using HPD.Auth.Admin.Models;
using HPD.Auth.ControlPlane;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin endpoints for user CRUD, search, and count.
///
/// Routes registered:
///   GET    /api/admin/users
///   GET    /api/admin/users/count
///   GET    /api/admin/users/{id}
///   POST   /api/admin/users
///   PUT    /api/admin/users/{id}
///   DELETE /api/admin/users/{id}
/// </summary>
public static class AdminUsersEndpoints
{
    public static void Map(RouteGroupBuilder root, IEndpointRouteBuilder app)
    {
        var group = root.MapGroup("/users");

        // ── List & Count ──────────────────────────────────────────────────────

        group.MapGet("/", ListUsersAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.IdentityRead)
             .WithName("AdminListUsers")
             .WithSummary("List users with optional filtering and pagination.");

        group.MapGet("/count", CountUsersAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.IdentityRead)
             .WithName("AdminCountUsers")
             .WithSummary("Count users matching the same filters as the list endpoint.");

        // ── Single User ───────────────────────────────────────────────────────

        group.MapGet("/{id}", GetUserAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.IdentityRead)
             .WithName("AdminGetUser")
             .WithSummary("Get a single user by ID.");

        group.MapPost("/", CreateUserAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.IdentityCreate)
             .WithName("AdminCreateUser")
             .WithSummary("Create a new user, optionally with a password, role, and email confirmation.");

        group.MapPut("/{id}", UpdateUserAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.IdentityWrite)
             .WithName("AdminUpdateUser")
             .WithSummary("Update mutable fields on an existing user. Only non-null fields are applied.");

        group.MapDelete("/{id}", DeleteUserAsync)
             .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.IdentityDelete)
             .WithName("AdminDeleteUser")
             .WithSummary("Tombstone a user and durably schedule its bounded retirement workflow.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListUsersAsync(
        UserManager<ApplicationUser> userManager,
        IAuthAdminUserQuery userQuery,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        string? search = null,
        string? email = null,
        bool? emailVerified = null,
        bool? enabled = null,
        string? role = null,
        int page = 1,
        int per_page = 50,
        string sort = "created_at",
        string order = "desc",
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        per_page = Math.Clamp(per_page, 1, 200);
        long offset = checked((long)(page - 1) * per_page);
        if (offset > 100_000)
            return Results.BadRequest(new { error = "The requested page exceeds the administrative query bound." });

        AuthAdminUserQueryResult queryResult = await userQuery.ExecuteAsync(new AuthAdminUserQuery
        {
            Search = search, Email = email, EmailVerified = emailVerified, Enabled = enabled, Role = role,
            Offset = checked((int)offset), Limit = per_page, Sort = ParseSort(sort), Direction = ParseDirection(order),
        }, ct);

        // Fetch roles for each user (N lookups — acceptable for admin pagination sizes).
        var responses = new List<AdminUserResponse>(queryResult.Users.Count);
        foreach (ApplicationUser u in queryResult.Users)
        {
            var roles = await userManager.GetRolesAsync(u);
            responses.Add(ToResponse(u, roles));
        }

        int totalPages = checked((int)Math.Ceiling(queryResult.Total / (double)per_page));
        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.UserList, correlationContext, cancellationToken: ct);
        return Results.Ok(new AdminUserListResponse(responses, queryResult.Total, page, per_page, totalPages));
    }

    private static async Task<IResult> CountUsersAsync(
        IAuthAdminUserQuery userQuery,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        string? search = null,
        string? email = null,
        bool? emailVerified = null,
        bool? enabled = null,
        string? role = null,
        CancellationToken ct = default)
    {
        AuthAdminUserQueryResult queryResult = await userQuery.ExecuteAsync(new AuthAdminUserQuery
        {
            Search = search, Email = email, EmailVerified = emailVerified, Enabled = enabled, Role = role,
            Offset = 0, Limit = 1, Sort = AuthAdminUserSort.CreatedAt,
            Direction = AuthAdminSortDirection.Descending,
        }, ct);
        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.UserCount, correlationContext, cancellationToken: ct);
        return Results.Ok(queryResult.Total);
    }

    private static async Task<IResult> GetUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out _))
            return Results.NotFound();

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var roles = await userManager.GetRolesAsync(user);
        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.UserView, correlationContext, user.Id, cancellationToken: ct);
        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<IResult> CreateUserAsync(
        AdminCreateUserRequest request,
        UserManager<ApplicationUser> userManager,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            SubscriptionTier = request.SubscriptionTier ?? "free",
            UserMetadata = request.UserMetadata ?? "{}",
            AppMetadata = request.AppMetadata ?? "{}",
        };

        IdentityResult result;
        if (!string.IsNullOrWhiteSpace(request.Password))
            result = await userManager.CreateAsync(user, request.Password);
        else
            result = await userManager.CreateAsync(user);

        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        // Auto-confirm email if requested.
        if (request.EmailConfirm == true)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmResult = await userManager.ConfirmEmailAsync(user, token);
            if (!confirmResult.Succeeded)
                return Results.BadRequest(confirmResult.Errors);

            user.EmailConfirmedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
        }

        // Optionally assign a role.
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var roleResult = await userManager.AddToRoleAsync(user, request.Role);
            if (!roleResult.Succeeded)
                return Results.BadRequest(roleResult.Errors);
        }

        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.UserCreate, correlationContext, user.Id, cancellationToken: ct);

        var roles = await userManager.GetRolesAsync(user);
        return Results.Created($"/api/admin/users/{user.Id}", ToResponse(user, roles));
    }

    private static async Task<IResult> UpdateUserAsync(
        string id,
        AdminUpdateUserRequest request,
        UserManager<ApplicationUser> userManager,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        // Apply only non-null fields.
        if (request.Email is not null)
        {
            user.Email = request.Email;
            user.UserName = request.Email;
        }
        if (request.EmailConfirmed.HasValue)
        {
            user.EmailConfirmed = request.EmailConfirmed.Value;
            if (request.EmailConfirmed.Value && user.EmailConfirmedAt is null)
                user.EmailConfirmedAt = DateTime.UtcNow;
        }
        if (request.FirstName is not null)
            user.FirstName = request.FirstName;
        if (request.LastName is not null)
            user.LastName = request.LastName;
        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName;
        if (request.SubscriptionTier is not null)
            user.SubscriptionTier = request.SubscriptionTier;
        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;
        if (request.UserMetadata is not null)
            user.UserMetadata = request.UserMetadata;
        if (request.AppMetadata is not null)
            user.AppMetadata = request.AppMetadata;
        if (request.RequiredActions is not null)
            user.RequiredActions = request.RequiredActions;

        user.Updated = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.UserUpdate, correlationContext, user.Id, cancellationToken: ct);

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<IResult> DeleteUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
            return Results.BadRequest(deleteResult.Errors);

        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.UserDelete, correlationContext, user.Id, cancellationToken: ct);

        return Results.NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers (internal — used by other endpoint files via ToResponse)
    // ─────────────────────────────────────────────────────────────────────────

    private static AuthAdminUserSort ParseSort(string sort) => sort switch
    {
        "email" => AuthAdminUserSort.Email,
        "last_login" => AuthAdminUserSort.LastLoginAt,
        _ => AuthAdminUserSort.CreatedAt,
    };

    private static AuthAdminSortDirection ParseDirection(string order) =>
        order.Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? AuthAdminSortDirection.Descending
            : AuthAdminSortDirection.Ascending;

    /// <summary>
    /// Map an <see cref="ApplicationUser"/> to the admin response DTO.
    /// </summary>
    internal static AdminUserResponse ToResponse(ApplicationUser user, IList<string> roles)
    {
        // IsLockedOut: LockoutEnd in the future means the user is currently locked out.
        bool isLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

        return new AdminUserResponse(
            Id: user.Id,
            Email: user.Email ?? string.Empty,
            EmailConfirmed: user.EmailConfirmed,
            FirstName: user.FirstName,
            LastName: user.LastName,
            DisplayName: user.DisplayName,
            SubscriptionTier: user.SubscriptionTier,
            IsActive: user.IsActive,
            IsDeleted: user.IsDeleted,
            LastLoginAt: user.LastLoginAt,
            LastLoginIp: user.LastLoginIp,
            Created: user.Created,
            Roles: roles,
            UserMetadata: user.UserMetadata,
            AppMetadata: user.AppMetadata,
            RequiredActions: user.RequiredActions,
            IsLockedOut: isLockedOut,
            LockoutEnd: user.LockoutEnd
        );
    }
}
