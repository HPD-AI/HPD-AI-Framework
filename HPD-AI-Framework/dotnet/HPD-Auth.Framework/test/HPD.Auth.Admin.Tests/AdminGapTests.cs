using FluentAssertions;
using HPD.Auth.Admin.Models;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Gap tests — real behaviours not covered by TESTS.md.
///
/// G1  Soft-deleted users excluded from list
/// G2  page=0 clamped to 1
/// G3  page=-1 clamped to 1
/// G4  per_page=0 clamped to 1
/// G5  Unknown sort value falls through to created_at (no 400)
/// G6  Generate-link URL-encoded token is decodable
/// G7  POST /ban with invalid duration → 400
/// G8  DELETE /sessions invalid userId → 400
/// G9  PasswordHash absent from list-users response
/// G10 PasswordHash absent from create-user response
/// G11 PasswordHash absent from update-user response
/// G12 Audit log Metadata is valid JSON for user.register
/// G13 Audit log Metadata is valid JSON for ban action
/// G14 Audit log Metadata is valid JSON for generate-link
/// </summary>
public class AdminGapTests : IAsyncLifetime
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

    // ── G1: Soft-deleted users excluded from list ─────────────────────────────

    /// <summary>
    /// NOTE: This test intentionally documents a known gap in the implementation.
    /// BuildFilterQuery does NOT filter IsDeleted=true users, so soft-deleted users
    /// DO currently appear in the list. This test will FAIL until the bug is fixed.
    /// Once fixed (add .Where(u => !u.IsDeleted) to BuildFilterQuery), this test passes.
    /// </summary>
    [Fact]
    public async Task ListUsers_SoftDeletedUser_NotReturnedInList()
    {
        var uniqueDomain = $"softdel-{Guid.NewGuid():N}.io";
        var user = await _factory.SeedUserAsync($"victim@{uniqueDomain}");

        // Soft-delete the user.
        await _admin.DeleteAsync($"/api/admin/users/{user.Id}?softDelete=true");

        var resp = await _admin.GetAsync($"/api/admin/users?search={uniqueDomain}");
        var body = await resp.ReadJsonAsync<AdminUserListResponse>();

        // A soft-deleted user should NOT appear in the list.
        body!.Users.Should().NotContain(u => u.Id == user.Id,
            "soft-deleted users must be excluded from the admin user list");
    }

    // ── G2: page=0 clamped to 1 ──────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_PageZero_ClampedToPage1()
    {
        var resp = await _admin.GetAsync("/api/admin/users?page=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Page.Should().Be(1);
    }

    // ── G3: page=-1 clamped to 1 ─────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_NegativePage_ClampedToPage1()
    {
        var resp = await _admin.GetAsync("/api/admin/users?page=-5");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.Page.Should().Be(1);
    }

    // ── G4: per_page=0 clamped to 1 ──────────────────────────────────────────

    [Fact]
    public async Task ListUsers_PerPageZero_ClampedTo1()
    {
        var resp = await _admin.GetAsync("/api/admin/users?per_page=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body!.PerPage.Should().Be(1);
    }

    // ── G5: Unknown sort value → no error, returns results ───────────────────

    [Fact]
    public async Task ListUsers_UnknownSortValue_Returns200WithResults()
    {
        await _factory.SeedUserAsync($"sortfallback@{Guid.NewGuid():N}.io");

        var resp = await _admin.GetAsync("/api/admin/users?sort=totally_made_up_field&order=asc");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonAsync<AdminUserListResponse>();
        body.Should().NotBeNull();
    }

    // ── G6: Generate-link token in actionLink is URL-decodable ───────────────

    [Fact]
    public async Task GenerateLink_TokenInActionLink_IsUrlDecodable()
    {
        await _factory.SeedUserAsync("urldecode@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "urldecode@example.com",
                RedirectTo: "https://app.example.com/reset"));

        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto.Should().NotBeNull();

        // Extract the raw encoded token from the actionLink.
        var uri = new Uri(dto!.ActionLink);
        var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var encodedToken = qs["token"];

        encodedToken.Should().NotBeNullOrEmpty("token must be present in actionLink");

        // URL-decode must not throw and must produce a non-empty value.
        var decoded = Uri.UnescapeDataString(encodedToken!);
        decoded.Should().NotBeNullOrEmpty("decoded token must be non-empty");
    }

    // ── G7: POST /ban with invalid duration format → 400 ─────────────────────

    [Fact]
    public async Task BanUser_InvalidDurationFormat_Returns400()
    {
        var user = await _factory.SeedUserAsync("badban@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "abc" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync();
        json.Should().Contain("Invalid duration format");
    }

    // ── G8: DELETE /sessions with invalid userId format → 400 ────────────────

    [Fact]
    public async Task RevokeSessions_InvalidUserIdFormat_Returns400()
    {
        var resp = await _admin.DeleteAsync("/api/admin/users/not-a-guid/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── G9: PasswordHash absent from list-users response ─────────────────────

    [Fact]
    public async Task ListUsers_ResponseDoesNotIncludePasswordHash()
    {
        await _factory.SeedUserAsync($"hashcheck-list@{Guid.NewGuid():N}.io",
            password: "Secure1!");

        var resp = await _admin.GetAsync("/api/admin/users");
        var json = await resp.Content.ReadAsStringAsync();

        json.ToLower().Should().NotContain("passwordhash");
        json.ToLower().Should().NotContain("password_hash");
    }

    // ── G10: PasswordHash absent from create-user response ───────────────────

    [Fact]
    public async Task CreateUser_ResponseDoesNotIncludePasswordHash()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest($"hashcheck-create@{Guid.NewGuid():N}.io",
                Password: "Secure1!"));

        var json = await resp.Content.ReadAsStringAsync();
        json.ToLower().Should().NotContain("passwordhash");
        json.ToLower().Should().NotContain("password_hash");
    }

    // ── G11: PasswordHash absent from update-user response ───────────────────

    [Fact]
    public async Task UpdateUser_ResponseDoesNotIncludePasswordHash()
    {
        var user = await _factory.SeedUserAsync($"hashcheck-update@{Guid.NewGuid():N}.io",
            password: "Secure1!");

        var resp = await _admin.PutJsonAsync($"/api/admin/users/{user.Id}",
            new AdminUpdateUserRequest(FirstName: "Updated"));

        var json = await resp.Content.ReadAsStringAsync();
        json.ToLower().Should().NotContain("passwordhash");
        json.ToLower().Should().NotContain("password_hash");
    }

    // ── G12: Audit log Metadata is valid JSON — user.register ────────────────

    [Fact]
    public async Task AuditLog_UserRegister_MetadataIsValidJson()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/users",
            new AdminCreateUserRequest($"metajson-create@{Guid.NewGuid():N}.io"));
        var dto = await resp.ReadJsonAsync<AdminUserResponse>();

        var logs = await _factory.GetAuditLogsAsync(
            userId: dto!.Id, action: AuditActions.UserRegister);
        logs.Should().NotBeEmpty();

        foreach (var log in logs)
        {
            var act = () => JsonDocument.Parse(log.Metadata);
            act.Should().NotThrow("audit log Metadata must be valid JSON");
        }
    }

    // ── G13: Audit log Metadata is valid JSON — ban action ───────────────────

    [Fact]
    public async Task AuditLog_BanUser_MetadataIsValidJson()
    {
        var user = await _factory.SeedUserAsync("metajson-ban@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "1h", reason = "testing" });

        var logs = await _factory.GetAuditLogsAsync(
            userId: user.Id, action: AuditActions.AccountLockout);
        logs.Should().NotBeEmpty();

        foreach (var log in logs)
        {
            var act = () => JsonDocument.Parse(log.Metadata);
            act.Should().NotThrow("audit log Metadata must be valid JSON");
        }
    }

    // ── G14: Audit log Metadata is valid JSON — generate-link ────────────────

    [Fact]
    public async Task AuditLog_GenerateLink_MetadataIsValidJson()
    {
        await _factory.SeedUserAsync("metajson-link@example.com");
        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "metajson-link@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();

        // Find the user to look up their logs.
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync("metajson-link@example.com");

        var logs = await _factory.GetAuditLogsAsync(userId: user!.Id);
        var linkLog = logs.FirstOrDefault(l => l.Metadata.Contains("generate_link"));
        linkLog.Should().NotBeNull();

        var act = () => JsonDocument.Parse(linkLog!.Metadata);
        act.Should().NotThrow("generate-link audit Metadata must be valid JSON");

        // hashedToken in metadata must not be the raw token.
        var parsed = JsonDocument.Parse(linkLog!.Metadata).RootElement;
        parsed.GetProperty("hashedToken").GetString().Should().Be(dto!.HashedToken);
    }
}
