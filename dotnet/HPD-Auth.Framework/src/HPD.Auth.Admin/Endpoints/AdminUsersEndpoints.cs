using HPD.Auth.Admin.Models;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        // ── List & Count ──────────────────────────────────────────────────────

        group.MapGet("/", ListUsersAsync)
             .WithName("AdminListUsers")
             .WithSummary("List users with optional filtering and pagination.");

        group.MapGet("/count", CountUsersAsync)
             .WithName("AdminCountUsers")
             .WithSummary("Count users matching the same filters as the list endpoint.");

        // ── Single User ───────────────────────────────────────────────────────

        group.MapGet("/{id}", GetUserAsync)
             .WithName("AdminGetUser")
             .WithSummary("Get a single user by ID.");

        group.MapPost("/", CreateUserAsync)
             .WithName("AdminCreateUser")
             .WithSummary("Create a new user, optionally with a password, role, and email confirmation.");

        group.MapPut("/{id}", UpdateUserAsync)
             .WithName("AdminUpdateUser")
             .WithSummary("Update mutable fields on an existing user. Only non-null fields are applied.");

        group.MapDelete("/{id}", DeleteUserAsync)
             .WithName("AdminDeleteUser")
             .WithSummary("Delete a user. Pass softDelete=true to soft-delete (sets IsDeleted flag).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListUsersAsync(
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
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
        per_page = Math.Clamp(per_page, 1, 500);

        var query = BuildFilterQuery(userManager, search, email, emailVerified, enabled);

        // Role filter requires a separate async lookup — resolve IDs first.
        if (!string.IsNullOrWhiteSpace(role))
        {
            var usersInRole = await userManager.GetUsersInRoleAsync(role);
            var roleUserIds = usersInRole.Select(u => u.Id).ToHashSet();
            query = query.Where(u => roleUserIds.Contains(u.Id));
        }

        query = ApplySorting(query, sort, order);

        var total = await query.CountAsync(ct);
        var users = await query
            .Skip((page - 1) * per_page)
            .Take(per_page)
            .ToListAsync(ct);

        // Fetch roles for each user (N lookups — acceptable for admin pagination sizes).
        var responses = new List<AdminUserResponse>(users.Count);
        foreach (var u in users)
        {
            var roles = await userManager.GetRolesAsync(u);
            responses.Add(ToResponse(u, roles));
        }

        var totalPages = (int)Math.Ceiling(total / (double)per_page);
        return Results.Ok(new AdminUserListResponse(responses, total, page, per_page, totalPages));
    }

    private static async Task<IResult> CountUsersAsync(
        UserManager<ApplicationUser> userManager,
        string? search = null,
        string? email = null,
        bool? emailVerified = null,
        bool? enabled = null,
        string? role = null,
        CancellationToken ct = default)
    {
        var query = BuildFilterQuery(userManager, search, email, emailVerified, enabled);

        if (!string.IsNullOrWhiteSpace(role))
        {
            var usersInRole = await userManager.GetUsersInRoleAsync(role);
            var roleUserIds = usersInRole.Select(u => u.Id).ToHashSet();
            query = query.Where(u => roleUserIds.Contains(u.Id));
        }

        var count = await query.CountAsync(ct);
        return Results.Ok(count);
    }

    private static async Task<IResult> GetUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out _))
            return Results.NotFound();

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<IResult> CreateUserAsync(
        AdminCreateUserRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
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

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.UserRegister,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "create_user", email = user.Email }
        ), ct);

        var roles = await userManager.GetRolesAsync(user);
        return Results.Created($"/api/admin/users/{user.Id}", ToResponse(user, roles));
    }

    private static async Task<IResult> UpdateUserAsync(
        string id,
        AdminUpdateUserRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
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

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserUpdate,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "update_user" }
        ), ct);

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToResponse(user, roles));
    }

    private static async Task<IResult> DeleteUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        bool softDelete = false,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        if (softDelete)
        {
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.Updated = DateTime.UtcNow;

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return Results.BadRequest(updateResult.Errors);
        }
        else
        {
            var deleteResult = await userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
                return Results.BadRequest(deleteResult.Errors);
        }

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserDelete,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = softDelete ? "soft_delete" : "hard_delete", email = user.Email }
        ), ct);

        return Results.NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers (internal — used by other endpoint files via ToResponse)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the base IQueryable with all supported filter predicates applied.
    /// The role filter is excluded here because it requires an async lookup.
    /// </summary>
    internal static IQueryable<ApplicationUser> BuildFilterQuery(
        UserManager<ApplicationUser> userManager,
        string? search,
        string? email,
        bool? emailVerified,
        bool? enabled)
    {
        var query = userManager.Users.Where(u => !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u =>
                (u.Email != null && u.Email.Contains(search)) ||
                (u.UserName != null && u.UserName.Contains(search)) ||
                (u.FirstName != null && u.FirstName.Contains(search)) ||
                (u.LastName != null && u.LastName.Contains(search)));

        if (!string.IsNullOrWhiteSpace(email))
            query = query.Where(u => u.Email != null && u.Email.Contains(email));

        if (emailVerified.HasValue)
            query = query.Where(u => u.EmailConfirmed == emailVerified.Value);

        if (enabled.HasValue)
            query = query.Where(u => u.IsActive == enabled.Value);

        return query;
    }

    private static IQueryable<ApplicationUser> ApplySorting(
        IQueryable<ApplicationUser> query,
        string sort,
        string order)
    {
        bool descending = order.Equals("desc", StringComparison.OrdinalIgnoreCase);

        return sort switch
        {
            "email"      => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "last_login" => descending ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderBy(u => u.LastLoginAt),
            // default: created_at
            _            => descending ? query.OrderByDescending(u => u.Created) : query.OrderBy(u => u.Created),
        };
    }

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
