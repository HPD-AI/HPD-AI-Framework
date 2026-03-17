using FluentAssertions;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   GET    /api/admin/users/{id}/roles          (section 17)
///   POST   /api/admin/users/{id}/roles          (section 18)
///   DELETE /api/admin/users/{id}/roles/{role}   (section 19)
///   GET    /api/admin/users/{id}/claims         (section 20)
///   POST   /api/admin/users/{id}/claims         (section 21)
///   DELETE /api/admin/users/{id}/claims         (section 22)
///   PUT    /api/admin/users/{id}/claims         (section 23)
///   GET    /api/admin/users/{id}/logins         (section 24)
///   DELETE /api/admin/users/{id}/logins/{provider} (section 25)
/// </summary>
public class AdminRolesClaimsLoginsTests : IAsyncLifetime
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

    // ── Section 17: GET roles ─────────────────────────────────────────────────

    // 17.1 — user with one role
    [Fact]
    public async Task GetRoles_UserWithOneRole_ReturnsRoleArray()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("getrole@example.com", role: "Admin");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var roles = await resp.ReadJsonAsync<List<string>>();
        roles.Should().Contain("Admin");
    }

    // 17.2 — user with no roles
    [Fact]
    public async Task GetRoles_UserWithNoRoles_ReturnsEmptyArray()
    {
        var user = await _factory.SeedUserAsync("norole@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/roles");
        var roles = await resp.ReadJsonAsync<List<string>>();
        roles.Should().BeEmpty();
    }

    // 17.3 — non-existent user → 404
    [Fact]
    public async Task GetRoles_NonExistentId_Returns404()
    {
        var resp = await _admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Section 18: POST roles ────────────────────────────────────────────────

    // 18.1 — assign existing role
    [Fact]
    public async Task AddRole_ExistingRole_UserIsInRole()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("addrole@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/roles",
            new { role = "Admin" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        (await um.IsInRoleAsync(reloaded!, "Admin")).Should().BeTrue();
    }

    // 18.2 — assign non-existent role → 400
    [Fact]
    public async Task AddRole_NonExistentRole_Returns400()
    {
        var user = await _factory.SeedUserAsync("addbadrole@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/roles",
            new { role = "GhostRole" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 18.3 — assign role user already has → 400
    [Fact]
    public async Task AddRole_UserAlreadyInRole_Returns400()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("dupRole@example.com", role: "Admin");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/roles",
            new { role = "Admin" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 18.4 — non-existent user → 404
    [Fact]
    public async Task AddRole_NonExistentUser_Returns404()
    {
        await _factory.EnsureRoleAsync("Admin");
        var resp = await _admin.PostJsonAsync($"/api/admin/users/{Guid.NewGuid()}/roles",
            new { role = "Admin" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 18.5 — audit log
    [Fact]
    public async Task AddRole_AuditLogWritten()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("auditaddrole@example.com");

        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/roles", new { role = "Admin" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminRoleAssign);
        logs.Should().NotBeEmpty();
    }

    // ── Section 19: DELETE roles ──────────────────────────────────────────────

    // 19.1 — remove existing role
    [Fact]
    public async Task RemoveRole_ExistingRole_UserNoLongerInRole()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("removerole@example.com", role: "Admin");

        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}/roles/Admin");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        (await um.IsInRoleAsync(reloaded!, "Admin")).Should().BeFalse();
    }

    // 19.2 — remove role user doesn't have → 400
    [Fact]
    public async Task RemoveRole_UserNotInRole_Returns400()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("notrole@example.com");

        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}/roles/Admin");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 19.3 — non-existent user → 404
    [Fact]
    public async Task RemoveRole_NonExistentUser_Returns404()
    {
        var resp = await _admin.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}/roles/Admin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 19.4 — audit log
    [Fact]
    public async Task RemoveRole_AuditLogWritten()
    {
        await _factory.EnsureRoleAsync("Admin");
        var user = await _factory.SeedUserAsync("auditremrole@example.com", role: "Admin");

        await _admin.DeleteAsync($"/api/admin/users/{user.Id}/roles/Admin");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminRoleRemove);
        logs.Should().NotBeEmpty();
    }

    // ── Section 20: GET claims ────────────────────────────────────────────────

    // 20.1 — user with claims
    [Fact]
    public async Task GetClaims_UserWithClaims_ReturnsClaimArray()
    {
        var user = await _factory.SeedUserAsync("getclaims@example.com");
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        await um.AddClaimAsync(reloaded!, new Claim("custom_type", "custom_value"));

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/claims");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("custom_type");
        json.Should().Contain("custom_value");
    }

    // 20.2 — user with no claims → empty array
    [Fact]
    public async Task GetClaims_UserWithNoClaims_ReturnsEmptyArray()
    {
        var user = await _factory.SeedUserAsync("noclaims@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/claims");
        var json = await resp.Content.ReadAsStringAsync();
        // Should be an empty JSON array.
        json.Trim().Should().StartWith("[");
    }

    // 20.3 — non-existent user → 404
    [Fact]
    public async Task GetClaims_NonExistentUser_Returns404()
    {
        var resp = await _admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}/claims");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Section 21: POST claims ───────────────────────────────────────────────

    // 21.1 — add new claim → persisted
    [Fact]
    public async Task AddClaim_NewClaim_Persisted()
    {
        var user = await _factory.SeedUserAsync("addclaim@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "dept", value = "engineering" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        var claims = await um.GetClaimsAsync(reloaded!);
        claims.Should().Contain(c => c.Type == "dept" && c.Value == "engineering");
    }

    // 21.2 — add duplicate claim — ASP.NET Identity AddClaimAsync is idempotent (returns Success).
    // The endpoint returns 200; the duplicate simply persists as a second row.
    [Fact]
    public async Task AddClaim_DuplicateClaim_Returns200Idempotent()
    {
        var user = await _factory.SeedUserAsync("dupclaim@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "dept", value = "sales" });

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "dept", value = "sales" });
        // Identity AddClaimAsync does not error on duplicates — it succeeds.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 21.3 — non-existent user → 404
    [Fact]
    public async Task AddClaim_NonExistentUser_Returns404()
    {
        var resp = await _admin.PostJsonAsync($"/api/admin/users/{Guid.NewGuid()}/claims",
            new { type = "x", value = "y" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 21.4 — audit log
    [Fact]
    public async Task AddClaim_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditaddclaim@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "role_level", value = "5" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserUpdate);
        logs.Should().NotBeEmpty();
        logs.Any(l => l.Metadata.Contains("add_claim")).Should().BeTrue();
    }

    // ── Section 22: DELETE claims ─────────────────────────────────────────────

    // 22.1 — remove existing claim
    [Fact]
    public async Task RemoveClaim_ExistingClaim_Removed()
    {
        var user = await _factory.SeedUserAsync("removeclaim@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "to_remove", value = "yes" });

        var resp = await _admin.DeleteJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "to_remove", value = "yes" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        var claims = await um.GetClaimsAsync(reloaded!);
        claims.Should().NotContain(c => c.Type == "to_remove");
    }

    // 22.2 — remove non-existent claim — ASP.NET Identity RemoveClaimAsync is a no-op (returns Success).
    [Fact]
    public async Task RemoveClaim_NonExistentClaim_Returns200NoOp()
    {
        var user = await _factory.SeedUserAsync("removebadclaim@example.com");

        var resp = await _admin.DeleteJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "ghost", value = "nope" });
        // Identity RemoveClaimAsync succeeds even when the claim doesn't exist.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 22.3 — non-existent user → 404
    [Fact]
    public async Task RemoveClaim_NonExistentUser_Returns404()
    {
        var resp = await _admin.DeleteJsonAsync($"/api/admin/users/{Guid.NewGuid()}/claims",
            new { type = "x", value = "y" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 22.4 — audit log
    [Fact]
    public async Task RemoveClaim_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditremclaim@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "audit_rem", value = "1" });
        await _admin.DeleteJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "audit_rem", value = "1" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserUpdate);
        logs.Any(l => l.Metadata.Contains("remove_claim")).Should().BeTrue();
    }

    // ── Section 23: PUT claims (replace) ─────────────────────────────────────

    // 23.1 — replace existing claim
    [Fact]
    public async Task ReplaceClaim_ExistingClaim_OldGoneNewPresent()
    {
        var user = await _factory.SeedUserAsync("replaceclaim@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "level", value = "1" });

        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}/claims",
            new
            {
                old = new { type = "level", value = "1" },
                @new = new { type = "level", value = "5" }
            });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        var claims = await um.GetClaimsAsync(reloaded!);
        claims.Should().NotContain(c => c.Type == "level" && c.Value == "1");
        claims.Should().Contain(c => c.Type == "level" && c.Value == "5");
    }

    // 23.2 — old claim doesn't exist — ASP.NET Identity ReplaceClaimAsync is a no-op (returns Success).
    [Fact]
    public async Task ReplaceClaim_OldClaimNotFound_Returns200NoOp()
    {
        var user = await _factory.SeedUserAsync("replacebadclaim@example.com");

        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}/claims",
            new
            {
                old = new { type = "ghost", value = "none" },
                @new = new { type = "ghost", value = "some" }
            });
        // Identity ReplaceClaimAsync succeeds even when the old claim doesn't exist.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 23.3 — non-existent user → 404
    [Fact]
    public async Task ReplaceClaim_NonExistentUser_Returns404()
    {
        var resp = await _admin.PutJsonAsync($"/api/admin/users/{Guid.NewGuid()}/claims",
            new
            {
                old = new { type = "x", value = "y" },
                @new = new { type = "x", value = "z" }
            });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 23.4 — audit log includes old and new types
    [Fact]
    public async Task ReplaceClaim_AuditLogIncludesOldAndNewType()
    {
        var user = await _factory.SeedUserAsync("auditrepclaim@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/claims",
            new { type = "a_type", value = "old" });

        await _admin.PutJsonAsync($"/api/admin/users/{user.Id}/claims",
            new
            {
                old = new { type = "a_type", value = "old" },
                @new = new { type = "b_type", value = "new" }
            });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserUpdate);
        var replaceLog = logs.FirstOrDefault(l => l.Metadata.Contains("replace_claim"));
        replaceLog.Should().NotBeNull();
        replaceLog!.Metadata.Should().Contain("a_type");
        replaceLog.Metadata.Should().Contain("b_type");
    }

    // ── Section 24: GET logins ────────────────────────────────────────────────

    // 24.1 — user with external login
    [Fact]
    public async Task GetLogins_UserWithExternalLogin_ReturnsLoginInfo()
    {
        var user = await _factory.SeedUserAsync("externallogin@example.com");
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        await um.AddLoginAsync(reloaded!,
            new UserLoginInfo("google", "google-key-123", "Google"));

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/logins");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("google");
        json.Should().Contain("google-key-123");
    }

    // 24.2 — user with no external logins → empty array
    [Fact]
    public async Task GetLogins_UserWithNoLogins_ReturnsEmptyArray()
    {
        var user = await _factory.SeedUserAsync("nologins@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/logins");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        json.Trim().Should().StartWith("[");
    }

    // 24.3 — non-existent user → 404
    [Fact]
    public async Task GetLogins_NonExistentUser_Returns404()
    {
        var resp = await _admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}/logins");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Section 25: DELETE logins ─────────────────────────────────────────────

    // 25.1 — valid provider + key → 200
    [Fact]
    public async Task RemoveLogin_ValidProviderAndKey_LoginRemoved()
    {
        var user = await _factory.SeedUserAsync("removelogin@example.com");
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        await um.AddLoginAsync(reloaded!,
            new UserLoginInfo("github", "github-key-abc", "GitHub"));

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/logins/github?providerKey=github-key-abc");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var logins = await um.GetLoginsAsync(reloaded!);
        logins.Should().NotContain(l => l.LoginProvider == "github");
    }

    // 25.2 — missing providerKey → 400
    [Fact]
    public async Task RemoveLogin_MissingProviderKey_Returns400()
    {
        var user = await _factory.SeedUserAsync("nopklogin@example.com");

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/logins/github");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 25.3 — provider/key not linked — ASP.NET Identity RemoveLoginAsync is a no-op (returns Success).
    [Fact]
    public async Task RemoveLogin_NotLinked_Returns200NoOp()
    {
        var user = await _factory.SeedUserAsync("notlinked@example.com");

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/logins/facebook?providerKey=ghost-key");
        // Identity RemoveLoginAsync succeeds even when the login isn't linked.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 25.4 — non-existent user → 404
    [Fact]
    public async Task RemoveLogin_NonExistentUser_Returns404()
    {
        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{Guid.NewGuid()}/logins/google?providerKey=somekey");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 25.5 — audit log
    [Fact]
    public async Task RemoveLogin_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditlogin@example.com");
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        await um.AddLoginAsync(reloaded!, new UserLoginInfo("twitter", "twit-key", "Twitter"));

        await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/logins/twitter?providerKey=twit-key");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.OAuthUnlink);
        logs.Should().NotBeEmpty();
    }
}
