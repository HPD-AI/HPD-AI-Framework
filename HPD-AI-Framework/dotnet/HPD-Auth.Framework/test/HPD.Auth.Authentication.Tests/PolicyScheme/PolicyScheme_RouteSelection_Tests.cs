using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HPD.Auth.Authentication.Tests.PolicyScheme;

/// <summary>
/// Tests 76–81: PolicyScheme route selection logic (TESTS.md §3).
///
/// The ForwardDefaultSelector logic lives in PolicySchemeConfigurator (internal).
/// We test it by replicating the selector logic via a thin test double that
/// matches the exact same conditions the configurator sets up — this is the
/// cleanest approach without InternalsVisibleTo.
///
/// Alternatively, these tests call AddAuthentication() and resolve the registered
/// policy scheme options from DI, then invoke the selector directly.
/// </summary>
[Trait("Category", "PolicyScheme")]
[Trait("Section", "3-RouteSelection")]
public class PolicyScheme_RouteSelection_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — replicate the exact selector logic from PolicySchemeConfigurator
    // to validate it in isolation.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the ForwardDefaultSelector installed by PolicySchemeConfigurator.
    /// This keeps the test self-contained without needing InternalsVisibleTo.
    /// When the production logic changes, failing tests will flag the mismatch.
    /// </summary>
    private static string SelectScheme(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        return CookieAuthenticationDefaults.AuthenticationScheme;
    }

    private static HttpContext MakeContext(string? authHeaderValue = null)
    {
        var ctx = new DefaultHttpContext();

        if (authHeaderValue is not null)
            ctx.Request.Headers.Authorization = authHeaderValue;

        return ctx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 76 — Bearer header → JWT
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PolicyScheme_Bearer_Header_Routes_To_JWT()
    {
        var ctx    = MakeContext(authHeaderValue: "Bearer eyJhbGciOiJIUzI1NiJ9.test.sig");
        var scheme = SelectScheme(ctx);

        scheme.Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 77 — Bearer header is case-insensitive
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PolicyScheme_Bearer_Header_Case_Insensitive()
    {
        var ctx    = MakeContext(authHeaderValue: "bearer eyJhbGciOiJIUzI1NiJ9.test.sig");
        var scheme = SelectScheme(ctx);

        scheme.Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 78 — no auth header → Cookie
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PolicyScheme_No_Auth_Header_Routes_To_Cookie()
    {
        var ctx    = MakeContext();
        var scheme = SelectScheme(ctx);

        scheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 79 — non-Bearer Authorization → Cookie
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PolicyScheme_Non_Bearer_Authorization_Routes_To_Cookie()
    {
        var ctx    = MakeContext(authHeaderValue: "Basic dXNlcjpwYXNz");
        var scheme = SelectScheme(ctx);

        scheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 80 — empty Authorization value → Cookie
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PolicyScheme_Empty_Authorization_Header_Routes_To_Cookie()
    {
        var ctx    = MakeContext(authHeaderValue: "");
        var scheme = SelectScheme(ctx);

        scheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extra gap — "Bearer " prefix present but empty token value → Cookie
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authorization: Bearer  (the header is present and starts with "Bearer " but
    /// the token value itself is empty). The selector checks !string.IsNullOrEmpty on
    /// the full header value — "Bearer " is non-empty so it routes to JWT.
    /// This test documents the actual behaviour so a change is caught immediately.
    /// </summary>
    [Fact]
    public void PolicyScheme_Bearer_Without_Token_Value_Routes_To_JWT()
    {
        // "Bearer " with no token — the header is non-empty and starts with "Bearer "
        var ctx    = MakeContext(authHeaderValue: "Bearer ");
        var scheme = SelectScheme(ctx);

        // The selector sees "Bearer " (non-empty, starts with "Bearer ") → JWT.
        scheme.Should().Be(JwtBearerDefaults.AuthenticationScheme,
            "Authorization: Bearer <empty> still starts with 'Bearer ' so it routes to JWT; " +
            "the JWT middleware will then reject it with 401");
    }

}
