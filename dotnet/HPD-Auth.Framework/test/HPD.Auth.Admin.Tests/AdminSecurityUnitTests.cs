using FluentAssertions;
using HPD.Auth.Admin.Endpoints;
using HPD.Auth.Admin.Models;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   Security — across all endpoints (section 36)
///   ParseBanDuration — unit tests (section 37)
///   AdminUserResponse mapping (section 38)
/// </summary>
public class AdminSecurityUnitTests : IAsyncLifetime
{
    private AdminWebFactory _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _anon = null!;
    private HttpClient _regular = null!;

    public async Task InitializeAsync()
    {
        _factory = new AdminWebFactory();
        await _factory.StartAsync();
        _admin = _factory.CreateAdminClient();
        _anon = _factory.CreateAnonymousClient();
        _regular = _factory.CreateRegularUserClient();
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _anon.Dispose();
        _regular.Dispose();
        await _factory.DisposeAsync();
    }

    // ── Section 36: Security ──────────────────────────────────────────────────

    // 36.1 — unauthenticated → 401
    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("GET", "/api/admin/users/count")]
    [InlineData("GET", "/api/admin/audit-logs")]
    public async Task Security_Unauthenticated_Returns401(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        var resp = await _anon.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 36.2 — regular user (no Admin role) → 403
    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("GET", "/api/admin/users/count")]
    [InlineData("GET", "/api/admin/audit-logs")]
    public async Task Security_RegularUser_Returns403(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        var resp = await _regular.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // 36.3 — authenticated Admin → never 401 or 403
    [Fact]
    public async Task Security_AuthenticatedAdmin_NeverReturns401Or403()
    {
        var resp = await _admin.GetAsync("/api/admin/users");
        ((int)resp.StatusCode).Should().NotBe(401);
        ((int)resp.StatusCode).Should().NotBe(403);
    }

    // 36.4 — audit logs written for every mutating operation
    [Fact]
    public async Task Security_MutatingOperationsWriteAuditLogs()
    {
        var user = await _factory.SeedUserAsync("sec-audit@example.com");

        // A sampling of mutating operations.
        await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);
        await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);
        await _admin.PostAsync($"/api/admin/users/{user.Id}/disable", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id);
        logs.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    // 36.5 — no "password" field in any audit log metadata
    [Fact]
    public async Task Security_AuditLogsDoNotContainPasswordField()
    {
        var user = await _factory.SeedUserAsync("sec-pw@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("SecureP@ss1"));

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id);
        foreach (var log in logs)
        {
            // The raw password value must never appear in metadata.
            log.Metadata.Should().NotContain("\"password\"");
            log.Metadata.Should().NotContain("SecureP@ss1");
        }
    }

    // 36.6 — raw token not in generate-link audit metadata
    [Fact]
    public async Task Security_GenerateLink_RawTokenNotInAuditLog()
    {
        var user = await _factory.SeedUserAsync("sec-token@example.com");
        var resp = await _admin.PostJsonAsync("/api/admin/generate-link",
            new AdminGenerateLinkRequest("recovery", "sec-token@example.com"));
        var dto = await resp.ReadJsonAsync<AdminGenerateLinkResponse>();

        // Extract the raw token from the link.
        var actionLink = dto!.ActionLink;
        var rawToken = System.Web.HttpUtility.ParseQueryString(
            actionLink.Contains('?')
                ? actionLink.Substring(actionLink.IndexOf('?'))
                : "?" + actionLink)["token"];

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id);
        foreach (var log in logs)
            if (!string.IsNullOrEmpty(rawToken))
                log.Metadata.Should().NotContain(rawToken);
    }

    // 36.7 — AdminUserResponse does not include PasswordHash
    [Fact]
    public async Task Security_AdminUserResponse_DoesNotIncludePasswordHash()
    {
        var user = await _factory.SeedUserAsync("sec-hash@example.com", password: "Secure1!");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}");
        var json = await resp.Content.ReadAsStringAsync();
        json.ToLower().Should().NotContain("passwordhash");
        json.ToLower().Should().NotContain("password_hash");
    }

    // ── Section 37: ParseBanDuration — unit tests ─────────────────────────────

    // 37.1 — "1h"
    [Fact]
    public void ParseBanDuration_1h_ReturnsOneHour()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("1h");
        result.Should().Be(TimeSpan.FromHours(1));
    }

    // 37.2 — "24h"
    [Fact]
    public void ParseBanDuration_24h_Returns24Hours()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("24h");
        result.Should().Be(TimeSpan.FromHours(24));
    }

    // 37.3 — "7d"
    [Fact]
    public void ParseBanDuration_7d_Returns7Days()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("7d");
        result.Should().Be(TimeSpan.FromDays(7));
    }

    // 37.4 — "30d"
    [Fact]
    public void ParseBanDuration_30d_Returns30Days()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("30d");
        result.Should().Be(TimeSpan.FromDays(30));
    }

    // 37.5 — "30m"
    [Fact]
    public void ParseBanDuration_30m_Returns30Minutes()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("30m");
        result.Should().Be(TimeSpan.FromMinutes(30));
    }

    // 37.6 — "120m"
    [Fact]
    public void ParseBanDuration_120m_Returns120Minutes()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("120m");
        result.Should().Be(TimeSpan.FromMinutes(120));
    }

    // 37.7 — "24:00:00" — .NET TimeSpan.Parse interprets this as d:hh:mm:ss → 24 days
    [Fact]
    public void ParseBanDuration_TimeSpanFormat_24_00_00_Returns24Days()
    {
        // TimeSpan.Parse("24:00:00") = 24 days (d:hh:mm:ss format), not 24 hours.
        var result = AdminUserActionsEndpoints.ParseBanDuration("24:00:00");
        result.Should().Be(TimeSpan.FromDays(24));
    }

    // 37.8 — "1.00:00:00" (1 day via TimeSpan)
    [Fact]
    public void ParseBanDuration_1DayTimeSpanFormat_Returns1Day()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("1.00:00:00");
        result.Should().Be(TimeSpan.FromDays(1));
    }

    // 37.9 — "0:30:00" (30 minutes via TimeSpan)
    [Fact]
    public void ParseBanDuration_30MinutesTimeSpanFormat_Returns30Minutes()
    {
        var result = AdminUserActionsEndpoints.ParseBanDuration("0:30:00");
        result.Should().Be(TimeSpan.FromMinutes(30));
    }

    // 37.10 — empty string → FormatException
    [Fact]
    public void ParseBanDuration_EmptyString_ThrowsFormatException()
    {
        var act = () => AdminUserActionsEndpoints.ParseBanDuration(string.Empty);
        act.Should().Throw<FormatException>();
    }

    // 37.11 — "abc" → FormatException
    [Fact]
    public void ParseBanDuration_Abc_ThrowsFormatException()
    {
        var act = () => AdminUserActionsEndpoints.ParseBanDuration("abc");
        act.Should().Throw<FormatException>();
    }

    // 37.12 — "xh" (non-numeric prefix with h suffix) → FormatException
    [Fact]
    public void ParseBanDuration_Xh_ThrowsFormatException()
    {
        var act = () => AdminUserActionsEndpoints.ParseBanDuration("xh");
        act.Should().Throw<FormatException>();
    }

    // ── Section 38: AdminUserResponse mapping ─────────────────────────────────

    // 38.1 — LockoutEnd in future → IsLockedOut=true
    [Fact]
    public void ToResponse_LockoutEndFuture_IsLockedOutTrue()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            LockoutEnd = DateTimeOffset.UtcNow.AddHours(1)
        };

        var response = AdminUsersEndpoints.ToResponse(user, new List<string>());
        response.IsLockedOut.Should().BeTrue();
    }

    // 38.2 — LockoutEnd in past → IsLockedOut=false
    [Fact]
    public void ToResponse_LockoutEndPast_IsLockedOutFalse()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            LockoutEnd = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var response = AdminUsersEndpoints.ToResponse(user, new List<string>());
        response.IsLockedOut.Should().BeFalse();
    }

    // 38.3 — LockoutEnd null → IsLockedOut=false
    [Fact]
    public void ToResponse_LockoutEndNull_IsLockedOutFalse()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            LockoutEnd = null
        };

        var response = AdminUsersEndpoints.ToResponse(user, new List<string>());
        response.IsLockedOut.Should().BeFalse();
    }

    // 38.4 — UserMetadata="{}" returned as-is
    [Fact]
    public void ToResponse_UserMetadataEmptyObject_ReturnedAsIs()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserMetadata = "{}"
        };

        var response = AdminUsersEndpoints.ToResponse(user, new List<string>());
        response.UserMetadata.Should().Be("{}");
    }

    // 38.5 — RequiredActions=["UPDATE_PASSWORD"] returned correctly
    [Fact]
    public void ToResponse_RequiredActions_ReturnedCorrectly()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            RequiredActions = new List<string> { "UPDATE_PASSWORD" }
        };

        var response = AdminUsersEndpoints.ToResponse(user, new List<string>());
        response.RequiredActions.Should().Contain("UPDATE_PASSWORD");
    }

    // 38.6 — Roles populated from provided list
    [Fact]
    public void ToResponse_RolesFromInput_PopulatedInResponse()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com"
        };
        var roles = new List<string> { "Admin", "Editor" };

        var response = AdminUsersEndpoints.ToResponse(user, roles);
        response.Roles.Should().BeEquivalentTo(roles);
    }
}
