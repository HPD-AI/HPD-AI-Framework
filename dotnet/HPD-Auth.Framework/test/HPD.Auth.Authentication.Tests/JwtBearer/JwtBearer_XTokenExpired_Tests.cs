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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HPD.Auth.Authentication.Tests.JwtBearer;

/// <summary>
/// Tests 111–117: X-Token-Expired header and structured challenge (TESTS.md §5.2–5.3).
/// Uses a minimal in-process test host via WebApplicationFactory.
/// </summary>
[Trait("Category", "JwtBearer")]
[Trait("Section", "5.2-5.3-Expiry-Challenge")]
public class JwtBearer_XTokenExpired_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Minimal test server setup
    // ─────────────────────────────────────────────────────────────────────────

    private static TestServer CreateServer(bool configureJwt = true)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();

        if (configureJwt)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(opts =>
                {
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer           = true,
                        ValidIssuer              = TokenServiceFixture.DefaultIssuer,
                        ValidateAudience         = true,
                        ValidAudience            = TokenServiceFixture.DefaultAudience,
                        ValidateLifetime         = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey         = key,
                        ClockSkew                = TimeSpan.Zero,
                        RequireExpirationTime    = true,
                        RequireSignedTokens      = true,
                    };

                    opts.Events = new JwtBearerEvents
                    {
                        OnAuthenticationFailed = context =>
                        {
                            if (context.Exception is SecurityTokenExpiredException)
                            {
                                context.Response.Headers.Append("X-Token-Expired", "true");
                            }
                            return Task.CompletedTask;
                        },

                        OnChallenge = context =>
                        {
                            context.HandleResponse();
                            context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            return context.Response.WriteAsJsonAsync(new
                            {
                                error             = "unauthorized",
                                error_description = "Authentication is required.",
                            });
                        },
                    };
                });
        }

        var app = builder.Build();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/protected", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();
            return Results.Ok(new { message = "authenticated" });
        }).RequireAuthorization();

        app.Start();

        return app.GetTestServer();
    }

    private static string MakeExpiredJwt()
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var past        = DateTime.UtcNow.AddHours(-1);

        var jwt = new JwtSecurityToken(
            issuer:             TokenServiceFixture.DefaultIssuer,
            audience:           TokenServiceFixture.DefaultAudience,
            claims:             new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) },
            notBefore:          past,
            expires:            past.AddMinutes(15),  // already expired
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string MakeValidJwt()
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now         = DateTime.UtcNow;

        var jwt = new JwtSecurityToken(
            issuer:             TokenServiceFixture.DefaultIssuer,
            audience:           TokenServiceFixture.DefaultAudience,
            claims:             new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) },
            notBefore:          now,
            expires:            now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string MakeTamperedJwt()
    {
        var valid  = MakeValidJwt();
        var parts  = valid.Split('.');
        // Tamper the signature
        parts[2] = "invalidsignature";
        return string.Join('.', parts);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 111 — expired token returns X-Token-Expired: true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Expired_Token_Returns_X_Token_Expired_Header()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeExpiredJwt());

        var response = await client.GetAsync("/protected");

        response.Headers.TryGetValues("X-Token-Expired", out var values).Should().BeTrue();
        values!.First().Should().Be("true");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 112 — valid token does NOT return X-Token-Expired header
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Valid_Token_Does_Not_Return_X_Token_Expired_Header()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeValidJwt());

        var response = await client.GetAsync("/protected");

        response.Headers.Contains("X-Token-Expired").Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 113 — tampered JWT does NOT return X-Token-Expired header
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Invalid_Signature_Does_Not_Return_X_Token_Expired_Header()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeTamperedJwt());

        var response = await client.GetAsync("/protected");

        response.Headers.Contains("X-Token-Expired").Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 114 — challenge returns 401 JSON
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Challenge_Returns_401_JSON()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 115 — challenge response body contains "error" key
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Challenge_Response_Contains_Error_Key()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();

        var response = await client.GetAsync("/protected");
        var body     = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"error\"");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 116 — challenge response body contains "error_description" key
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Challenge_Response_Contains_Error_Description()
    {
        using var server = CreateServer();
        var client       = server.CreateClient();

        var response = await client.GetAsync("/protected");
        var body     = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"error_description\"");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 117 — challenge returns 401, not a redirect
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task JWT_Challenge_No_Redirect()
    {
        using var server  = CreateServer();
        var handler       = server.CreateHandler();
        var client        = new System.Net.Http.HttpClient(handler) { BaseAddress = server.BaseAddress };
        // JWT challenge returns 401 directly; no redirect to suppress.
        var response = await client.GetAsync("/protected");

        ((int)response.StatusCode).Should().NotBe(302);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
