using FluentAssertions;
using HPD.Auth.Admin.Models;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   POST /api/admin/generate-link          (section 33)
///   GET  /api/admin/audit-logs             (section 34)
///   GET  /api/admin/users/{id}/audit-logs  (section 35)
/// </summary>
public class AdminLinksAuditTests : IAsyncLifetime
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

    // ── Section 33: Generate Link ─────────────────────────────────────────────

    // 33.1 — type="signup"
    [Fact]
    public async Task GenerateLink_Signup_ReturnsActionLink()
    {
        var user = await _factory.SeedUserAsync("link-signup@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("signup", "link-signup@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.VerificationType.Should().Be("signup");
        dto.ActionLink.Should().Contain("token=");
        dto.ActionLink.Should().Contain($"userId={user.Id}");
        dto.ActionLink.Should().Contain("type=signup");
    }

    // 33.2 — type="invite"
    [Fact]
    public async Task GenerateLink_Invite_ReturnsActionLink()
    {
        await _factory.SeedUserAsync("link-invite@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("invite", "link-invite@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.VerificationType.Should().Be("invite");
    }

    // 33.3 — type="magiclink"
    [Fact]
    public async Task GenerateLink_MagicLink_ReturnsActionLink()
    {
        await _factory.SeedUserAsync("link-magic@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("magiclink", "link-magic@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.VerificationType.Should().Be("magiclink");
    }

    // 33.4 — type="recovery"
    [Fact]
    public async Task GenerateLink_Recovery_ReturnsActionLink()
    {
        await _factory.SeedUserAsync("link-recovery@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "link-recovery@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.VerificationType.Should().Be("recovery");
    }

    // 33.5 — type="verify_email"
    [Fact]
    public async Task GenerateLink_VerifyEmail_ReturnsActionLink()
    {
        await _factory.SeedUserAsync("link-verify@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("verify_email", "link-verify@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.VerificationType.Should().Be("verify_email");
    }

    // 33.6 — redirectTo provided → link is redirectTo?token=...
    [Fact]
    public async Task GenerateLink_WithRedirectTo_LinkStartsWithRedirect()
    {
        await _factory.SeedUserAsync("link-redirect@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest(
                "signup",
                "link-redirect@example.com",
                RedirectTo: "https://app.example.com/callback"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.ActionLink.Should().StartWith("https://app.example.com/callback?token=");
    }

    // 33.7 — redirectTo already has query params → separator is &
    [Fact]
    public async Task GenerateLink_RedirectToWithExistingParams_UsesAmpersandSeparator()
    {
        await _factory.SeedUserAsync("link-amp@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest(
                "signup",
                "link-amp@example.com",
                RedirectTo: "https://app.example.com/cb?source=email"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.ActionLink.Should().StartWith("https://app.example.com/cb?source=email&token=");
    }

    // 33.8 — redirectTo omitted → link starts with ?token=
    [Fact]
    public async Task GenerateLink_NoRedirectTo_LinkStartsWithQueryString()
    {
        await _factory.SeedUserAsync("link-noredirect@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("signup", "link-noredirect@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.ActionLink.Should().StartWith("?token=");
    }

    // 33.9 — response includes HashedToken (64-char hex)
    [Fact]
    public async Task GenerateLink_HashedTokenIsSha256Hex()
    {
        await _factory.SeedUserAsync("link-hash@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "link-hash@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.HashedToken.Should().HaveLength(64);
        dto.HashedToken.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // 33.10 — hashedToken != raw token
    [Fact]
    public async Task GenerateLink_HashedTokenDiffersFromRawToken()
    {
        await _factory.SeedUserAsync("link-rawhash@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "link-rawhash@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();

        // Extract raw token from the actionLink.
        var uri = new Uri("https://fake.example.com" + (dto!.ActionLink.StartsWith("?")
            ? dto.ActionLink
            : "?" + dto.ActionLink.Split('?').Last()));
        var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var rawToken = qs["token"];

        rawToken.Should().NotBeNullOrEmpty();
        dto.HashedToken.Should().NotBe(rawToken);
    }

    // 33.11 — actionLink contains userId
    [Fact]
    public async Task GenerateLink_ActionLinkContainsUserId()
    {
        var user = await _factory.SeedUserAsync("link-userid@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("signup", "link-userid@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.ActionLink.Should().Contain($"userId={user.Id}");
    }

    // 33.12 — actionLink contains type
    [Fact]
    public async Task GenerateLink_ActionLinkContainsType()
    {
        await _factory.SeedUserAsync("link-type@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "link-type@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();
        dto!.ActionLink.Should().Contain("type=recovery");
    }

    // 33.13 — unknown email → 404
    [Fact]
    public async Task GenerateLink_UnknownEmail_Returns404()
    {
        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("signup", "ghost@notexist.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 33.14 — unsupported type → 400
    [Fact]
    public async Task GenerateLink_UnsupportedType_Returns400WithValidTypes()
    {
        await _factory.SeedUserAsync("link-badtype@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("teleport", "link-badtype@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync();
        // Response should list valid types.
        json.Should().ContainAny("signup", "recovery", "magiclink", "verify_email");
    }

    // 33.15 — audit log written without raw token
    [Fact]
    public async Task GenerateLink_AuditLogWrittenWithHashedToken()
    {
        var user = await _factory.SeedUserAsync("link-audit@example.com");

        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "link-audit@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id);
        logs.Should().NotBeEmpty();
        var linkLog = logs.First(l => l.Metadata.Contains("generate_link"));
        linkLog.Metadata.Should().Contain(dto!.HashedToken);
    }

    // ── Section 34: GET /api/admin/audit-logs ─────────────────────────────────

    // 34.1 — no filters returns all logs
    [Fact]
    public async Task QueryAuditLogs_NoFilters_Returns200WithLogs()
    {
        var user = await _factory.SeedUserAsync("auditquery@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/unban", null);

        var resp = await _admin.GetAsync("/api/admin/audit-logs");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
    }

    // 34.2 — userId filter
    [Fact]
    public async Task QueryAuditLogs_UserIdFilter_ReturnsOnlyThatUsersLogs()
    {
        var user = await _factory.SeedUserAsync("auditfilter@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);

        var resp = await _admin.GetAsync($"/api/admin/audit-logs?userId={user.Id}");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
    }

    // 34.3 — action filter
    [Fact]
    public async Task QueryAuditLogs_ActionFilter_ReturnsOnlyMatchingActions()
    {
        var user = await _factory.SeedUserAsync("auditaction@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);

        var resp = await _admin.GetAsync(
            $"/api/admin/audit-logs?userId={user.Id}&action={AuditActions.EmailConfirm}");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("count").GetInt32().Should().BeGreaterThan(0);

        // All returned logs should have the correct action.
        foreach (var log in json.GetProperty("logs").EnumerateArray())
            log.GetProperty("action").GetString().Should().Be(AuditActions.EmailConfirm);
    }

    // 34.4 — category filter
    [Fact]
    public async Task QueryAuditLogs_CategoryFilter_ReturnsOnlyAdminCategory()
    {
        var user = await _factory.SeedUserAsync("auditcat@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);

        var resp = await _admin.GetAsync(
            $"/api/admin/audit-logs?userId={user.Id}&category={AuditCategories.Admin}");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        foreach (var log in json.GetProperty("logs").EnumerateArray())
            log.GetProperty("category").GetString().Should().Be(AuditCategories.Admin);
    }

    // 34.6 — date range filter from / to
    [Fact]
    public async Task QueryAuditLogs_DateRangeFilter_ReturnsLogsInRange()
    {
        var user = await _factory.SeedUserAsync("auditdate@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);

        var from = DateTime.UtcNow.AddMinutes(-1).ToString("o");
        var to = DateTime.UtcNow.AddMinutes(1).ToString("o");

        var resp = await _admin.GetAsync(
            $"/api/admin/audit-logs?userId={user.Id}&from={from}&to={to}");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
    }

    // 34.9 — pagination
    [Fact]
    public async Task QueryAuditLogs_Pagination_ReturnsCorrectPage()
    {
        // Generate a few logs.
        var user = await _factory.SeedUserAsync("auditpagetest@example.com");
        for (int i = 0; i < 5; i++)
            await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);

        var resp = await _admin.GetAsync(
            $"/api/admin/audit-logs?userId={user.Id}&page=1&per_page=2");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("perPage").GetInt32().Should().Be(2);
        json.GetProperty("logs").GetArrayLength().Should().BeLessThanOrEqualTo(2);
    }

    // 34.10 — per_page=1000 clamped to 500
    [Fact]
    public async Task QueryAuditLogs_OverCapPerPage_ClampedTo500()
    {
        var resp = await _admin.GetAsync("/api/admin/audit-logs?per_page=1000");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("perPage").GetInt32().Should().Be(500);
    }

    // ── Section 35: GET /api/admin/users/{id}/audit-logs ─────────────────────

    // 35.1 — user with audit trail
    [Fact]
    public async Task GetUserAuditLogs_UserWithLogs_ReturnsOnlyThatUserLogs()
    {
        var user = await _factory.SeedUserAsync("useraudittrail@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/audit-logs");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
    }

    // 35.2 — user with no audit entries → empty list
    [Fact]
    public async Task GetUserAuditLogs_UserWithNoLogs_ReturnsEmpty()
    {
        var user = await _factory.SeedUserAsync("nologs@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/audit-logs");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("count").GetInt32().Should().Be(0);
    }

    // 35.3 — invalid user ID format → 400
    [Fact]
    public async Task GetUserAuditLogs_InvalidUserIdFormat_Returns400()
    {
        var resp = await _admin.GetAsync("/api/admin/users/not-a-guid/audit-logs");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 35.4 — filter by action within user trail
    [Fact]
    public async Task GetUserAuditLogs_ActionFilter_ReturnsSubset()
    {
        var user = await _factory.SeedUserAsync("userauditaction@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);
        await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);

        var resp = await _admin.GetAsync(
            $"/api/admin/users/{user.Id}/audit-logs?action={AuditActions.EmailConfirm}");
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var logs = json.GetProperty("logs").EnumerateArray().ToList();
        logs.Should().OnlyContain(l => l.GetProperty("action").GetString() == AuditActions.EmailConfirm);
    }
}
