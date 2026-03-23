using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HPD.Auth.Authentication.Tests.Integration;

/// <summary>
/// Test 154: Integration_Token_Tied_To_Correct_InstanceId (TESTS.md §8.3).
///
/// Verifies that a JWT minted for one tenant (InstanceId A) cannot be used to
/// authenticate as a user of a different tenant (InstanceId B). This is the
/// token-isolation edge-case for single-tenant servers that each expect their
/// own audience / issuer pair.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Section", "8.3-MultiTenant")]
public class Integration_MultiTenant_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Two separate servers that share the same signing key but use different
    // audiences — one per "tenant". This is the simplest model for demonstrating
    // token isolation without requiring a custom ITenantContext override.
    // ─────────────────────────────────────────────────────────────────────────

    private static TestServer CreateTenantServer(string audience)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));

        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = TokenServiceFixture.DefaultIssuer,
                    ValidateAudience         = true,
                    ValidAudience            = audience,          // tenant-specific
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = key,
                    ClockSkew                = TimeSpan.Zero,
                    RequireExpirationTime    = true,
                    RequireSignedTokens      = true,
                };
            });

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/protected", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { authenticated = true })
                : Results.Unauthorized()
        ).RequireAuthorization();

        app.Start();
        return app.GetTestServer();
    }

    private static string MintTokenForAudience(string audience)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now         = DateTime.UtcNow;

        var jwt = new JwtSecurityToken(
            issuer:             TokenServiceFixture.DefaultIssuer,
            audience:           audience,
            claims:             new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) },
            notBefore:          now,
            expires:            now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 154 — token for tenant A cannot authenticate against tenant B server
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A JWT minted for tenant-A (audience = "tenant-a") should be rejected by
    /// the tenant-B server (audience = "tenant-b") even though both share the
    /// same signing key.
    /// </summary>
    [Fact]
    public async Task Integration_Token_Tied_To_Correct_InstanceId()
    {
        const string tenantAAudience = "tenant-a";
        const string tenantBAudience = "tenant-b";

        using var serverA = CreateTenantServer(tenantAAudience);
        using var serverB = CreateTenantServer(tenantBAudience);

        var tokenForA = MintTokenForAudience(tenantAAudience);
        var tokenForB = MintTokenForAudience(tenantBAudience);

        // Token A → server A: must succeed.
        var clientA = serverA.CreateClient();
        clientA.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenForA);
        var responseAA = await clientA.GetAsync("/protected");
        responseAA.StatusCode.Should().Be(HttpStatusCode.OK,
            "tenant-A token must authenticate against tenant-A server");

        // Token A → server B: must fail.
        var clientAB = serverB.CreateClient();
        clientAB.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenForA);
        var responseAB = await clientAB.GetAsync("/protected");
        responseAB.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "tenant-A token must be rejected by tenant-B server");

        // Token B → server B: must succeed.
        var clientB = serverB.CreateClient();
        clientB.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenForB);
        var responseBB = await clientB.GetAsync("/protected");
        responseBB.StatusCode.Should().Be(HttpStatusCode.OK,
            "tenant-B token must authenticate against tenant-B server");
    }
}
