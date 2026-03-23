using System.Net;
using FluentAssertions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.Extensions;

/// <summary>
/// Test 133: CookieOnlyMode_Cookie_Auth_Works_End_To_End (TESTS.md §6).
///
/// Exercises the full sign-in → authenticated request flow when the application
/// is configured in cookie-only mode (no JWT secret). Uses a real in-process
/// TestServer with cookie handling enabled on the HttpClient.
/// </summary>
[Trait("Category", "Extensions")]
[Trait("Section", "6-CookieOnly-EndToEnd")]
public class CookieOnlyMode_EndToEnd_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test server — cookie-only mode (Jwt.Secret = null)
    // ─────────────────────────────────────────────────────────────────────────

    private static (TestServer Server, IServiceProvider RootServices) CreateCookieOnlyServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var dbName = Guid.NewGuid().ToString();

        builder.Services.AddHPDAuth(opts =>
        {
            opts.AppName    = dbName;
            opts.Jwt.Secret = null;  // cookie-only mode
        })
        .AddAuthentication();

        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapPost("/auth/login", async (HttpContext ctx) =>
        {
            var userId      = ctx.Request.Query["user"].ToString();
            var userManager = ctx.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var factory     = ctx.RequestServices
                .GetRequiredService<Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<ApplicationUser>>();
            var user        = await userManager.FindByIdAsync(userId);
            if (user is null) return Results.NotFound();
            var principal = await factory.CreateAsync(user);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Ok();
        });

        app.MapGet("/protected", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { authenticated = true })
                : Results.Unauthorized()
        ).RequireAuthorization();

        app.Start();
        return (app.GetTestServer(), app.Services);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 133 — cookie-only mode end-to-end: sign in, then access protected resource
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In cookie-only mode (no JWT secret): signing in via SignInManager should
    /// issue an auth cookie, and a subsequent request with that cookie should
    /// succeed with a 200 response and IsAuthenticated = true.
    /// </summary>
    [Fact]
    public async Task CookieOnlyMode_Cookie_Auth_Works_End_To_End()
    {
        var (server, rootServices) = CreateCookieOnlyServer();
        using var _ = server;

        // Create a user.
        using var scope = rootServices.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope);

        // Cookie-preserving client.
        var client = server.CreateCookieClient();

        // 1. Sign in via SignInManager — should return 200 and set the auth cookie.
        var loginResponse = await client.PostAsync($"/auth/login?user={user.Id}", null);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "sign-in must succeed in cookie-only mode");

        // 2. Subsequent request with cookie → 200.
        var protectedResponse = await client.GetAsync("/protected");
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "authenticated cookie should allow access to protected endpoint");
    }
}
