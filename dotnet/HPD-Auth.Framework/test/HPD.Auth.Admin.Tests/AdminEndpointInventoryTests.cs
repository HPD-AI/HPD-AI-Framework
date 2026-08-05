using FluentAssertions;
using HPD.Auth.Admin.Tests.Helpers;
using Xunit;
using System.Net;
using System.Text.Json;

namespace HPD.Auth.Admin.Tests;

public sealed class AdminEndpointInventoryTests
{
    [Fact]
    public async Task Exact_L1_route_capability_inventory_is_installed()
    {
        await using var factory = new AdminWebFactory();

        factory.GetAdminEndpointInventory().Should().BeEquivalentTo(Expected, options =>
            options.WithStrictOrdering());
    }

    [Fact]
    public async Task Anonymous_control_plane_failure_is_fixed_problem_details()
    {
        await using var factory = new AdminWebFactory();
        await factory.StartAsync();
        using var response = await factory.CreateAnonymousClient().GetAsync("/api/admin/users/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString()
            .Should().Be("hpd.auth.authenticationRequired");
        body.RootElement.ToString().Should().NotContain("RequireAdmin");
    }

    [Fact]
    public async Task Forbidden_control_plane_failure_is_fixed_problem_details()
    {
        await using var factory = new AdminWebFactory();
        await factory.StartAsync();
        using var response = await factory.CreateRegularUserClient().GetAsync("/api/admin/users/");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString()
            .Should().Be("hpd.auth.accessDenied");
        body.RootElement.ToString().Should().NotContain("Admin");
    }

    private static readonly (string Method, string Route, string Capability)[] Expected =
    [
        ("DELETE", "/api/admin/users/{id}", "auth.identity.delete"),
        ("DELETE", "/api/admin/users/{id}/2fa/authenticator", "auth.credentials.write"),
        ("DELETE", "/api/admin/users/{id}/claims", "auth.authorization.write"),
        ("DELETE", "/api/admin/users/{id}/logins/{provider}", "auth.credentials.write"),
        ("DELETE", "/api/admin/users/{id}/password", "auth.credentials.write"),
        ("DELETE", "/api/admin/users/{id}/roles/{role}", "auth.authorization.write"),
        ("DELETE", "/api/admin/users/{id}/sessions", "auth.sessions.write"),
        ("DELETE", "/api/admin/users/{id}/sessions/{sessionId}", "auth.sessions.write"),
        ("GET", "/api/admin/audit-logs", "auth.audit.read"),
        ("GET", "/api/admin/users/", "auth.identity.read"),
        ("GET", "/api/admin/users/count", "auth.identity.read"),
        ("GET", "/api/admin/users/{id}", "auth.identity.read"),
        ("GET", "/api/admin/users/{id}/2fa", "auth.credentials.read"),
        ("GET", "/api/admin/users/{id}/audit-logs", "auth.audit.read"),
        ("GET", "/api/admin/users/{id}/claims", "auth.authorization.read"),
        ("GET", "/api/admin/users/{id}/logins", "auth.credentials.read"),
        ("GET", "/api/admin/users/{id}/roles", "auth.authorization.read"),
        ("GET", "/api/admin/users/{id}/sessions", "auth.sessions.read"),
        ("POST", "/api/admin/generate-link", "auth.credentials.issue"),
        ("POST", "/api/admin/users/", "auth.identity.create"),
        ("POST", "/api/admin/users/{id}/2fa/disable", "auth.credentials.write"),
        ("POST", "/api/admin/users/{id}/2fa/recovery-codes", "auth.credentials.write"),
        ("POST", "/api/admin/users/{id}/ban", "auth.identity.write"),
        ("POST", "/api/admin/users/{id}/claims", "auth.authorization.write"),
        ("POST", "/api/admin/users/{id}/disable", "auth.identity.write"),
        ("POST", "/api/admin/users/{id}/enable", "auth.identity.write"),
        ("POST", "/api/admin/users/{id}/invalidate-sessions", "auth.sessions.write"),
        ("POST", "/api/admin/users/{id}/password", "auth.credentials.write"),
        ("POST", "/api/admin/users/{id}/reset-password", "auth.credentials.write"),
        ("POST", "/api/admin/users/{id}/roles", "auth.authorization.write"),
        ("POST", "/api/admin/users/{id}/unban", "auth.identity.write"),
        ("POST", "/api/admin/users/{id}/unlock", "auth.identity.write"),
        ("POST", "/api/admin/users/{id}/verify-email", "auth.identity.write"),
        ("PUT", "/api/admin/users/{id}", "auth.identity.write"),
        ("PUT", "/api/admin/users/{id}/claims", "auth.authorization.write")
    ];
}
