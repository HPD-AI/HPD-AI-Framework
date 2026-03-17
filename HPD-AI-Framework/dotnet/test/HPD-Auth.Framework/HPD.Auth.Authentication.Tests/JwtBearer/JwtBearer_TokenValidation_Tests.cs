using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HPD.Auth.Authentication.Tests.JwtBearer;

/// <summary>
/// Tests 106–110: Core JWT token validation (TESTS.md §5.1).
/// Uses a minimal in-process TestServer — same pattern as JwtBearer_XTokenExpired_Tests.
/// </summary>
[Trait("Category", "JwtBearer")]
[Trait("Section", "5.1-Token-Validation")]
public class JwtBearer_TokenValidation_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Minimal test server
    // ─────────────────────────────────────────────────────────────────────────

    private static TestServer CreateServer(
        string issuer   = TokenServiceFixture.DefaultIssuer,
        string audience = TokenServiceFixture.DefaultAudience,
        bool requireExpiry = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = issuer,
                    ValidateAudience         = true,
                    ValidAudience            = audience,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = key,
                    ClockSkew                = TimeSpan.Zero,
                    RequireExpirationTime    = requireExpiry,
                    RequireSignedTokens      = true,
                };
            });

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/protected", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();
            return Results.Ok(new { authenticated = true, sub = ctx.User.FindFirstValue(JwtRegisteredClaimNames.Sub) });
        }).RequireAuthorization();

        app.Start();
        return app.GetTestServer();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JWT helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string MakeValidJwt(
        string? issuer   = null,
        string? audience = null,
        bool includeExpiry = true)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now         = DateTime.UtcNow;
        var sub         = Guid.NewGuid().ToString();

        var jwt = new JwtSecurityToken(
            issuer:             issuer ?? TokenServiceFixture.DefaultIssuer,
            audience:           audience ?? TokenServiceFixture.DefaultAudience,
            claims:             new[] { new Claim(JwtRegisteredClaimNames.Sub, sub) },
            notBefore:          now,
            expires:            includeExpiry ? now.AddMinutes(15) : null,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string MakeTamperedSignatureJwt()
    {
        var parts  = MakeValidJwt().Split('.');
        parts[2]   = "invalidsignature000000000000000000000000000";
        return string.Join('.', parts);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 106 — valid token → 200, HttpContext.User populated
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A properly-issued, unexpired, correctly-signed JWT should result in a
    /// 200 response with an authenticated HttpContext.User.
    /// </summary>
    [Fact]
    public async Task JWT_Valid_Token_Authenticates_Successfully()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeValidJwt());

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("authenticated");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 107 — tampered signature → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A token whose signature has been modified should be rejected with 401.
    /// </summary>
    [Fact]
    public async Task JWT_Invalid_Signature_Fails()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeTamperedSignatureJwt());

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 108 — wrong issuer → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A token issued by a different issuer should be rejected even when the
    /// signature is valid (signed with the same key but wrong issuer claim).
    /// </summary>
    [Fact]
    public async Task JWT_Wrong_Issuer_Fails()
    {
        using var server = CreateServer(); // expects DefaultIssuer
        var client       = server.CreateClient();
        var wrongIssuerToken = MakeValidJwt(issuer: "https://evil.attacker.example");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", wrongIssuerToken);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 109 — wrong audience → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A token with an audience that doesn't match the server's expected audience
    /// should be rejected with 401.
    /// </summary>
    [Fact]
    public async Task JWT_Wrong_Audience_Fails()
    {
        using var server = CreateServer(); // expects DefaultAudience
        var client       = server.CreateClient();
        var wrongAudienceToken = MakeValidJwt(audience: "some-other-app");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", wrongAudienceToken);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 110 — missing exp claim when RequireExpirationTime = true → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When RequireExpirationTime = true a token that lacks the 'exp' claim
    /// should be rejected. This prevents long-lived tokens being crafted without
    /// an expiry.
    /// </summary>
    [Fact]
    public async Task JWT_Missing_Expiry_Fails_When_RequireExpirationTime_True()
    {
        using var server = CreateServer(requireExpiry: true);
        var client       = server.CreateClient();
        var noExpiryToken = MakeValidJwt(includeExpiry: false);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", noExpiryToken);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
