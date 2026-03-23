using System.IdentityModel.Tokens.Jwt;
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

namespace HPD.Auth.Authentication.Tests.Integration;

/// <summary>
/// Tests 142–145: Full-stack browser / cookie authentication flow (TESTS.md §8.1).
///
/// Uses a real in-process test server (AddHPDAuth + AddAuthentication) and an
/// HttpClient with cookie handling enabled to exercise the complete
/// sign-in → authenticated request → sign-out → force-logout cycle.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Section", "8.1-Cookie-Flow")]
public class Integration_Cookie_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared test server factory
    // ─────────────────────────────────────────────────────────────────────────

    private static (TestServer Server, IServiceProvider RootServices) CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var dbName = Guid.NewGuid().ToString();

        builder.Services.AddHPDAuth(opts =>
        {
            opts.AppName                     = dbName;
            opts.Jwt.Secret                  = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer                  = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience                = TokenServiceFixture.DefaultAudience;
            opts.Cookie.CookieName           = ".HPDTest.Auth";
            opts.Cookie.UseSlidingExpiration = false;
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
            // Sign in explicitly via the HPD Cookies scheme (not the Identity.Application scheme
            // that SignInManager uses by default) so that the HPD policy scheme can authenticate it.
            var principal = await factory.CreateAsync(user);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Ok();
        });

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        app.MapGet("/protected", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { authenticated = true, name = ctx.User.Identity.Name })
                : Results.Unauthorized()
        ).RequireAuthorization();

        app.Start();
        return (app.GetTestServer(), app.Services);
    }

    private static HttpClient CreateCookieClient(TestServer server) =>
        server.CreateCookieClient();

    private static async Task<(HttpClient Client, ApplicationUser User)> SignInAsync(
        TestServer server,
        IServiceProvider rootServices)
    {
        using var scope = rootServices.CreateScope();
        var user   = await ServiceProviderBuilder.CreateUserAsync(scope);
        var client = CreateCookieClient(server);

        var response = await client.PostAsync($"/auth/login?user={user.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "sign-in must succeed");
        return (client, user);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 142 — cookie login sets Set-Cookie header
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST to the login endpoint via SignInManager should return a Set-Cookie
    /// header containing the authentication cookie.
    /// </summary>
    [Fact]
    public async Task Integration_Cookie_Login_Sets_Cookie()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        using var scope = rootServices.CreateScope();
        var user   = await ServiceProviderBuilder.CreateUserAsync(scope);
        var client = CreateCookieClient(server);

        var response = await client.PostAsync($"/auth/login?user={user.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").Should().BeTrue(
            "sign-in should issue the authentication cookie");

        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        setCookie.Should().Contain(".HPDTest.Auth",
            "the cookie name should match configuration");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 143 — subsequent request with cookie authenticates
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After sign-in the HttpClient retains the auth cookie. A subsequent GET to
    /// a protected endpoint should return 200 with IsAuthenticated = true.
    /// </summary>
    [Fact]
    public async Task Integration_Cookie_Subsequent_Request_Authenticates()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _) = await SignInAsync(server, rootServices);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("authenticated");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 144 — logout clears the cookie
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After sign-out the response should contain a Set-Cookie header that
    /// expires the authentication cookie. Subsequent requests must not be authenticated.
    /// </summary>
    [Fact]
    public async Task Integration_Cookie_Logout_Clears_Cookie()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _) = await SignInAsync(server, rootServices);

        // Confirm authenticated.
        var before = await client.GetAsync("/protected");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sign out.
        var logoutResponse = await client.PostAsync("/auth/logout", null);
        logoutResponse.Headers.Contains("Set-Cookie").Should().BeTrue(
            "sign-out should expire the cookie via Set-Cookie");

        // Subsequent request must no longer be authenticated.
        var after = await client.GetAsync("/protected");
        ((int)after.StatusCode).Should().NotBe(200,
            "after logout the cookie must no longer authenticate the user");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 145 — force-logout via security stamp rotation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After an admin rotates the security stamp (simulating a password change /
    /// admin revocation) the existing cookie should be rejected on the next request.
    /// </summary>
    [Fact]
    public async Task Integration_Cookie_Force_Logout_Via_SecurityStamp()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, user) = await SignInAsync(server, rootServices);

        // Rotate the security stamp.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.UpdateSecurityStampAsync(refreshed!);
        }

        var response = await client.GetAsync("/protected");

        ((int)response.StatusCode).Should().NotBe(200,
            "a rotated security stamp must force the cookie to be rejected");
    }
}
