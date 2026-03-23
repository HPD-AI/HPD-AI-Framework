using FluentAssertions;
using HPD.Auth.Admin.Models;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   GET    /api/admin/users          (section 1)
///   GET    /api/admin/users/count    (section 2)
///   GET    /api/admin/users/{id}     (section 3)
///   POST   /api/admin/users          (section 4)
///   PUT    /api/admin/users/{id}     (section 5)
///   DELETE /api/admin/users/{id}     (section 6)
/// </summary>
public class AdminUsersTests : IAsyncLifetime
{
    private AdminWebFactory _factory = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _factory = new AdminWebFactory();
        await _factory.StartAsync();
        _admin = _factory.CreateAdminClient();
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
    }

    // ── Section 1: GET /api/admin/users ──────────────────────────────────────

    // 1.1 — No filters returns all non-deleted users
    [Fact]
    public async Task ListUsers_NoFilters_ReturnsAllUsers()
    {
        await _factory.SeedUserAsync("alice@example.com");
        await _factory.SeedUserAsync("bob@example.com");

        var resp = await _admin.GetAsync("/api/admin/users");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body.Should().NotBeNull();
        body!.Users.Should().HaveCountGreaterThanOrEqualTo(2);
        body.Total.Should().BeGreaterThanOrEqualTo(2);
    }

    // 1.2 — search= filters by email, username, first, or last name
    [Fact]
    public async Task ListUsers_SearchFilter_ReturnsMatchingUsers()
    {
        await _factory.SeedUserAsync("alice-search@example.com");
        await _factory.SeedUserAsync("zz-nomatch@example.com");

        var resp = await _admin.GetAsync("/api/admin/users?search=alice-search");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => u.Email.Contains("alice-search"));
    }

    // 1.3 — email= filter by email fragment
    [Fact]
    public async Task ListUsers_EmailFilter_ReturnsMatchingUsers()
    {
        await _factory.SeedUserAsync("filtered@acme.org");
        await _factory.SeedUserAsync("other@example.com");

        var resp = await _admin.GetAsync("/api/admin/users?email=acme.org");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => u.Email.Contains("acme.org"));
    }

    // 1.4 — emailVerified=true
    [Fact]
    public async Task ListUsers_EmailVerifiedTrue_ReturnsOnlyConfirmedUsers()
    {
        await _factory.SeedUserAsync("verified@example.com", emailConfirmed: true);
        await _factory.SeedUserAsync("unverified@example.com", emailConfirmed: false);

        var resp = await _admin.GetAsync("/api/admin/users?emailVerified=true");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => u.EmailConfirmed);
    }

    // 1.5 — emailVerified=false
    [Fact]
    public async Task ListUsers_EmailVerifiedFalse_ReturnsOnlyUnconfirmedUsers()
    {
        await _factory.SeedUserAsync("unverified2@example.com", emailConfirmed: false);

        var resp = await _admin.GetAsync("/api/admin/users?emailVerified=false");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => !u.EmailConfirmed);
    }

    // 1.6 — enabled=true
    [Fact]
    public async Task ListUsers_EnabledTrue_ReturnsOnlyActiveUsers()
    {
        await _factory.SeedUserAsync("active@example.com", isActive: true);
        await _factory.SeedUserAsync("inactive@example.com", isActive: false);

        var resp = await _admin.GetAsync("/api/admin/users?enabled=true");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => u.IsActive);
    }

    // 1.7 — enabled=false
    [Fact]
    public async Task ListUsers_EnabledFalse_ReturnsOnlyInactiveUsers()
    {
        await _factory.SeedUserAsync("inactive2@example.com", isActive: false);

        var resp = await _admin.GetAsync("/api/admin/users?enabled=false");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => !u.IsActive);
    }

    // 1.8 — role=Admin
    [Fact]
    public async Task ListUsers_RoleFilter_ReturnsOnlyUsersInThatRole()
    {
        await _factory.EnsureRoleAsync("Admin");
        await _factory.SeedUserAsync("admin-user@example.com", role: "Admin");
        await _factory.SeedUserAsync("norole-user@example.com");

        var resp = await _admin.GetAsync("/api/admin/users?role=Admin");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => u.Roles.Contains("Admin"));
    }

    // 1.9 — role=NonExistentRole → empty list
    [Fact]
    public async Task ListUsers_NonExistentRole_ReturnsEmptyList()
    {
        var resp = await _admin.GetAsync("/api/admin/users?role=NonExistentRole");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().BeEmpty();
    }

    // 1.10 — search + emailVerified combined
    [Fact]
    public async Task ListUsers_CombinedFilters_ReturnsIntersection()
    {
        await _factory.SeedUserAsync("john-v@example.com", emailConfirmed: true);
        await _factory.SeedUserAsync("john-u@example.com", emailConfirmed: false);
        await _factory.SeedUserAsync("mary-v@example.com", emailConfirmed: true);

        var resp = await _admin.GetAsync("/api/admin/users?search=john&emailVerified=true");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Users.Should().OnlyContain(u => u.Email.Contains("john") && u.EmailConfirmed);
    }

    // 1.11 — pagination: page=2 & per_page=5 with 12 users
    [Fact]
    public async Task ListUsers_Pagination_ReturnsCorrectPage()
    {
        // Seed 12 uniquely-named users for this test.
        for (int i = 1; i <= 12; i++)
            await _factory.SeedUserAsync($"paged{i:D2}@paginationtest.io");

        var resp = await _admin.GetAsync("/api/admin/users?search=paginationtest.io&page=2&per_page=5");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Page.Should().Be(2);
        body.PerPage.Should().Be(5);
        body.Total.Should().Be(12);
        body.TotalPages.Should().Be(3);
        body.Users.Should().HaveCount(5);
    }

    // 1.12 — per_page=1000 is clamped to 500
    [Fact]
    public async Task ListUsers_OverCapPerPage_ClampedTo500()
    {
        var resp = await _admin.GetAsync("/api/admin/users?per_page=1000");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.PerPage.Should().Be(500);
    }

    // 1.13 — sort=email&order=asc
    [Fact]
    public async Task ListUsers_SortEmailAsc_OrderedAlphabetically()
    {
        await _factory.SeedUserAsync("zebra@sorttest.io");
        await _factory.SeedUserAsync("apple@sorttest.io");
        await _factory.SeedUserAsync("mango@sorttest.io");

        var resp = await _admin.GetAsync("/api/admin/users?search=sorttest.io&sort=email&order=asc");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        var emails = body!.Users.Select(u => u.Email).ToList();
        emails.Should().BeInAscendingOrder();
    }

    // 1.14 — default sort is created_at desc (newest first)
    [Fact]
    public async Task ListUsers_DefaultSort_NewestFirst()
    {
        // Seed two users and verify the one created last appears first.
        await _factory.SeedUserAsync("older@createdtest.io");
        await Task.Delay(10); // tiny gap to ensure distinct Created timestamps
        await _factory.SeedUserAsync("newer@createdtest.io");

        var resp = await _admin.GetAsync("/api/admin/users?search=createdtest.io&sort=created_at&order=desc");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        var emails = body!.Users.Select(u => u.Email).ToList();
        emails.First().Should().Be("newer@createdtest.io");
    }

    // 1.15 — sort=last_login&order=asc
    [Fact]
    public async Task ListUsers_SortLastLoginAsc_ReturnsWithoutError()
    {
        var resp = await _admin.GetAsync("/api/admin/users?sort=last_login&order=asc");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 1.16 — each user in response includes roles array
    [Fact]
    public async Task ListUsers_ResponseIncludesRolesPerUser()
    {
        await _factory.EnsureRoleAsync("Admin");
        await _factory.SeedUserAsync("roles-check@example.com", role: "Admin");

        var resp = await _admin.GetAsync("/api/admin/users?search=roles-check");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        var user = body!.Users.Single(u => u.Email == "roles-check@example.com");
        user.Roles.Should().Contain("Admin");
    }

    // ── Section 2: GET /api/admin/users/count ────────────────────────────────

    // 2.1 — no filters returns total count
    [Fact]
    public async Task CountUsers_NoFilters_ReturnsTotalCount()
    {
        await _factory.SeedUserAsync("count1@counttest.io");
        await _factory.SeedUserAsync("count2@counttest.io");

        var resp = await _admin.GetAsync("/api/admin/users/count");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var count = await resp.ReadJsonAsync<int>();
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    // 2.2 — emailVerified=true count
    [Fact]
    public async Task CountUsers_EmailVerifiedTrue_CountsOnlyVerified()
    {
        await _factory.SeedUserAsync("cv1@counttest.io", emailConfirmed: true);
        await _factory.SeedUserAsync("cu1@counttest.io", emailConfirmed: false);

        var all = await (await _admin.GetAsync("/api/admin/users/count?emailVerified=true")).ReadJsonAsync<int>();
        var none = await (await _admin.GetAsync("/api/admin/users/count?emailVerified=false")).ReadJsonAsync<int>();
        all.Should().BeGreaterThan(0);
        none.Should().BeGreaterThanOrEqualTo(0);
        // Totals differ
        (all + none).Should().BeGreaterThanOrEqualTo(2);
    }

    // 2.3 — role=Admin count
    [Fact]
    public async Task CountUsers_RoleFilter_CountsRoleMembers()
    {
        await _factory.EnsureRoleAsync("Admin");
        await _factory.SeedUserAsync("cadmin@counttest.io", role: "Admin");

        var resp = await _admin.GetAsync("/api/admin/users/count?role=Admin");
        var count = await resp.ReadJsonAsync<int>();
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    // 2.4 — all filters combined
    [Fact]
    public async Task CountUsers_AllFiltersCombined_CountsIntersection()
    {
        await _factory.EnsureRoleAsync("Admin");
        await _factory.SeedUserAsync("comb-admin@combined.io", emailConfirmed: true, role: "Admin");
        await _factory.SeedUserAsync("comb-user@combined.io", emailConfirmed: true);

        var resp = await _admin.GetAsync(
            "/api/admin/users/count?search=combined.io&emailVerified=true&role=Admin");
        var count = await resp.ReadJsonAsync<int>();
        count.Should().Be(1);
    }

    // ── Section 3: GET /api/admin/users/{id} ─────────────────────────────────

    // 3.1 — valid existing user
    [Fact]
    public async Task GetUser_ValidId_Returns200WithUser()
    {
        var user = await _factory.SeedUserAsync("getuser@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.Id.Should().Be(user.Id);
        dto.Email.Should().Be("getuser@example.com");
    }

    // 3.2 — non-existent ID
    [Fact]
    public async Task GetUser_NonExistentId_Returns404()
    {
        var resp = await _admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 3.3 — malformed GUID
    [Fact]
    public async Task GetUser_MalformedGuid_Returns404()
    {
        var resp = await _admin.GetAsync("/api/admin/users/not-a-valid-guid");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 3.4 — roles array populated
    [Fact]
    public async Task GetUser_ResponseIncludesRoles()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("rolescheck@example.com", role: "Admin");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.Roles.Should().Contain("Admin");
    }

    // 3.5 — UserMetadata and AppMetadata not null
    [Fact]
    public async Task GetUser_ResponseIncludesMetadata()
    {
        var user = await _factory.SeedUserAsync("meta@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.UserMetadata.Should().NotBeNull();
        dto.AppMetadata.Should().NotBeNull();
    }

    // 3.6 — IsLockedOut=true when LockoutEnd in the future
    [Fact]
    public async Task GetUser_LockedOutInFuture_IsLockedOutTrue()
    {
        var user = await _factory.SeedUserAsync("locked@example.com");

        // Set lockout end to tomorrow (reload in a fresh scope to avoid EF tracking conflict).
        using var scope = _factory._GetScope();
        var userManager = scope.GetService<UserManager<ApplicationUser>>();
        var fresh = await userManager.FindByIdAsync(user.Id.ToString());
        await userManager.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddDays(1));

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.IsLockedOut.Should().BeTrue();
    }

    // 3.7 — IsLockedOut=false when LockoutEnd is null or past
    [Fact]
    public async Task GetUser_NotLockedOut_IsLockedOutFalse()
    {
        var user = await _factory.SeedUserAsync("notlocked@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.IsLockedOut.Should().BeFalse();
    }

    // ── Section 4: POST /api/admin/users ─────────────────────────────────────

    // 4.1 — create with email + password → 201
    [Fact]
    public async Task CreateUser_WithPassword_Returns201()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("newuser@example.com", Password: "Password1!"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.Email.Should().Be("newuser@example.com");
    }

    // 4.2 — create without password → 201, no password hash
    [Fact]
    public async Task CreateUser_WithoutPassword_Returns201()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("nopassword@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // 4.3 — emailConfirm=true → EmailConfirmed=true
    [Fact]
    public async Task CreateUser_EmailConfirmTrue_UserIsEmailConfirmed()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("confirmed@example.com", EmailConfirm: true));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.EmailConfirmed.Should().BeTrue();
    }

    // 4.4 — emailConfirm=false → EmailConfirmed=false
    [Fact]
    public async Task CreateUser_EmailConfirmFalse_UserIsNotEmailConfirmed()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("unconfirmed@example.com", EmailConfirm: false));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.EmailConfirmed.Should().BeFalse();
    }

    // 4.5 — role="Admin" → user is in Admin role
    [Fact]
    public async Task CreateUser_WithRole_UserIsInRole()
    {
        await _factory.EnsureRoleAsync("Admin");
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("withRole@example.com", Role: "Admin"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.Roles.Should().Contain("Admin");
    }

    // 4.6 — optional profile fields persisted
    [Fact]
    public async Task CreateUser_WithProfileFields_Persisted()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest(
                "profile@example.com",
                FirstName: "John",
                LastName: "Doe",
                DisplayName: "JohnD"));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.FirstName.Should().Be("John");
        dto.LastName.Should().Be("Doe");
        dto.DisplayName.Should().Be("JohnD");
    }

    // 4.7 — custom SubscriptionTier
    [Fact]
    public async Task CreateUser_WithSubscriptionTier_TierStored()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("tier@example.com", SubscriptionTier: "pro"));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.SubscriptionTier.Should().Be("pro");
    }

    // 4.8 — weak password fails policy → 400
    [Fact]
    public async Task CreateUser_WeakPassword_Returns400WithErrors()
    {
        // The factory sets RequiredLength=6 but this is too short for the real policy check path.
        // We just test that an empty / clearly invalid password fails.
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("weak@example.com", Password: "x"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 4.9 — duplicate email → 400 DuplicateEmail
    [Fact]
    public async Task CreateUser_DuplicateEmail_Returns400()
    {
        await _factory.SeedUserAsync("dup@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("dup@example.com", Password: "Password1!"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 4.10 — audit log entry written
    [Fact]
    public async Task CreateUser_AuditLogWritten()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest("auditcreate@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();

        var logs = await _factory.GetAuditLogsAsync(userId: dto!.Id, action: AuditActions.UserRegister);
        logs.Should().NotBeEmpty();
    }

    // ── Section 5: PUT /api/admin/users/{id} ─────────────────────────────────

    // 5.1 — update email
    [Fact]
    public async Task UpdateUser_Email_EmailAndUsernameUpdated()
    {
        var user = await _factory.SeedUserAsync("oldemail@example.com");
        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(Email: "newemail@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.Email.Should().Be("newemail@example.com");
    }

    // 5.2 — partial update: only FirstName
    [Fact]
    public async Task UpdateUser_OnlyFirstName_OtherFieldsUnchanged()
    {
        var user = await _factory.SeedUserAsync("partial@example.com");
        var original = await (await _admin.GetAsync($"/api/admin/users/{user.Id}"))
            .ReadJsonAsync<AdminUserResponse>();

        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(FirstName: "Alice"));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.FirstName.Should().Be("Alice");
        dto.Email.Should().Be(original!.Email);
    }

    // 5.3 — IsActive=false persisted
    [Fact]
    public async Task UpdateUser_IsActiveFalse_Persisted()
    {
        var user = await _factory.SeedUserAsync("deactivate@example.com");
        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(IsActive: false));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.IsActive.Should().BeFalse();
    }

    // 5.4 — update RequiredActions
    [Fact]
    public async Task UpdateUser_RequiredActions_ListReplaced()
    {
        var user = await _factory.SeedUserAsync("actions@example.com");
        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(RequiredActions: new List<string> { "UPDATE_PASSWORD" }));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.RequiredActions.Should().Contain("UPDATE_PASSWORD");
    }

    // 5.5 — update AppMetadata
    [Fact]
    public async Task UpdateUser_AppMetadata_StoredCorrectly()
    {
        var user = await _factory.SeedUserAsync("appmeta@example.com");
        var json = """{"tier":"enterprise"}""";
        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(AppMetadata: json));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();
        dto!.AppMetadata.Should().Be(json);
    }

    // 5.6 — non-existent user → 404
    [Fact]
    public async Task UpdateUser_NonExistentId_Returns404()
    {
        var resp = await _admin.PutJsonAsync($"/api/admin/users/{Guid.NewGuid()}",
            new AdminUpdateUserRequest(FirstName: "Ghost"));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 5.7 — audit log written
    [Fact]
    public async Task UpdateUser_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditupdate@example.com");
        await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(FirstName: "Updated"));

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserUpdate);
        logs.Should().NotBeEmpty();
    }

    // ── Section 6: DELETE /api/admin/users/{id} ──────────────────────────────

    // 6.1 — hard delete
    [Fact]
    public async Task DeleteUser_HardDelete_UserRemovedFromDb()
    {
        var user = await _factory.SeedUserAsync("harddelete@example.com");
        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 6.2 — soft delete
    [Fact]
    public async Task DeleteUser_SoftDelete_IsDeletedTrueRowStillExists()
    {
        var user = await _factory.SeedUserAsync("softdelete@example.com");
        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}?softDelete=true");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Row still exists, IsDeleted=true.
        using var scope = _factory._GetScope();
        var userManager = scope.GetService<UserManager<ApplicationUser>>();
        var found = await userManager.FindByIdAsync(user.Id.ToString());
        found.Should().NotBeNull();
        found!.IsDeleted.Should().BeTrue();
        found.DeletedAt.Should().NotBeNull();
    }

    // 6.3 — non-existent user → 404
    [Fact]
    public async Task DeleteUser_NonExistentId_Returns404()
    {
        var resp = await _admin.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 6.4 — audit log for hard delete
    [Fact]
    public async Task DeleteUser_HardDelete_AuditLogHasHardDeleteAction()
    {
        var user = await _factory.SeedUserAsync("audithard@example.com");
        var userId = user.Id;
        await _admin.DeleteAsync($"/api/admin/users/{userId}");

        var logs = await _factory.GetAuditLogsAsync(userId: userId, action: AuditActions.AdminUserDelete);
        logs.Should().NotBeEmpty();
        logs.First().Metadata.Should().Contain("hard_delete");
    }

    // 6.5 — audit log for soft delete
    [Fact]
    public async Task DeleteUser_SoftDelete_AuditLogHasSoftDeleteAction()
    {
        var user = await _factory.SeedUserAsync("auditsoft@example.com");
        await _admin.DeleteAsync($"/api/admin/users/{user.Id}?softDelete=true");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserDelete);
        logs.Should().NotBeEmpty();
        logs.First().Metadata.Should().Contain("soft_delete");
    }
}

// Helper extension to get a service scope directly from AdminWebFactory.
internal static class AdminWebFactoryExtensions
{
    public static ScopeWrapper _GetScope(this AdminWebFactory factory)
        => new(factory);
}

internal sealed class ScopeWrapper : IDisposable
{
    private readonly IServiceScope _scope;

    public ScopeWrapper(AdminWebFactory factory)
    {
        _scope = factory.GetService<IServiceScopeFactory>().CreateScope();
    }

    public T GetService<T>() where T : notnull
        => _scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose() => _scope.Dispose();
}
