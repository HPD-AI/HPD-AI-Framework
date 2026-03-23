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

namespace HPD.Auth.Authentication.Tests.Cookie;

/// <summary>
/// Tests 101–105: Cookie security stamp validation and sliding expiration (TESTS.md §4.3–4.4).
///
/// These tests spin up a full HPD.Auth in-process server (AddHPDAuth + AddAuthentication)
/// and drive it through an HttpClient that persists cookies across requests, mirroring a
/// real browser session.
/// </summary>
[Trait("Category", "Cookie")]
[Trait("Section", "4.3-4.4-SecurityStamp-Sliding")]
public class CookieAuthentication_SecurityStamp_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test host factory
    // ─────────────────────────────────────────────────────────────────────────

    private static (TestServer Server, IServiceProvider RootServices) CreateServer(
        bool useSlidingExpiration = true,
        TimeSpan? slidingDuration = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var dbName = Guid.NewGuid().ToString();

        builder.Services.AddHPDAuth(opts =>
        {
            opts.AppName                        = dbName;
            opts.Jwt.Secret                     = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer                     = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience                   = TokenServiceFixture.DefaultAudience;
            opts.Cookie.UseSlidingExpiration    = useSlidingExpiration;
            opts.Cookie.SlidingExpiration       = slidingDuration ?? TimeSpan.FromMinutes(30);
        })
        .AddAuthentication();

        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Sign-in endpoint — accepts ?user=<id> and calls SignInManager.
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

        // Protected endpoint.
        app.MapGet("/protected", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { authenticated = true })
                : Results.Unauthorized()
        ).RequireAuthorization();

        // Sign-out endpoint.
        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        app.Start();
        return (app.GetTestServer(), app.Services);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper — create a user and sign in; returns an HttpClient with the cookie set.
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<(HttpClient Client, ApplicationUser User)> SignInAsync(
        TestServer server,
        IServiceProvider rootServices)
    {
        using var scope = rootServices.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope);

        // Create a cookie-preserving client.
        // TestServer.CreateHandler() returns the in-process HttpMessageHandler.
        // We wrap it with a CookieContainerHandler to simulate a browser cookie jar.
        var client = server.CreateCookieClient();

        var loginResponse = await client.PostAsync($"/auth/login?user={user.Id}", null);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, "sign-in must succeed");

        return (client, user);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 101 — valid security stamp → 200
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A freshly issued cookie whose security stamp has not changed should allow
    /// subsequent authenticated requests.
    /// </summary>
    [Fact]
    public async Task Cookie_ValidSecurityStamp_Allows_Authenticated_Request()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _) = await SignInAsync(server, rootServices);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 102 — rotated security stamp → OnValidatePrincipal rejects → non-200
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After an admin rotates the user's security stamp (e.g., password reset),
    /// the next request with the old cookie should be rejected.
    /// The cookie middleware calls RejectPrincipal() and signs out, so the client
    /// either gets a redirect (302) or a 401, but not 200.
    /// </summary>
    [Fact]
    public async Task Cookie_SecurityStamp_Changed_Rejects_Principal()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, user) = await SignInAsync(server, rootServices);

        // Rotate the security stamp — simulates a password change or admin force-logout.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.UpdateSecurityStampAsync(refreshed!);
        }

        var response = await client.GetAsync("/protected");

        // The cookie middleware calls RejectPrincipal() + SignOutAsync().
        // AllowAutoRedirect = false so we see the raw status code.
        // It will be either 302 (redirect to login) or 401 (if caught by authorization).
        ((int)response.StatusCode).Should().NotBe(200,
            "a rejected cookie principal must not produce a 200 OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 103 — sign-out invalidates the cookie immediately
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After explicit sign-out the cookie should be expired/cleared.
    /// Subsequent requests to a protected endpoint should no longer be authenticated.
    /// </summary>
    [Fact]
    public async Task Cookie_SignOut_Invalidates_Session()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _) = await SignInAsync(server, rootServices);

        // Confirm authenticated before logout.
        var before = await client.GetAsync("/protected");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sign out.
        await client.PostAsync("/auth/logout", null);

        // Subsequent request should no longer be authenticated.
        var after = await client.GetAsync("/protected");
        ((int)after.StatusCode).Should().NotBe(200,
            "after sign-out the cookie must no longer authenticate");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 104 — SlidingExpiration renews Set-Cookie on authenticated request
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When UseSlidingExpiration = true the cookie is configured to slide.
    /// ASP.NET Core only emits a Set-Cookie renewal when more than 50% of the
    /// ExpireTimeSpan has elapsed, so we verify the configuration is applied
    /// and the request still succeeds — the timing-based renewal is covered by
    /// the configuration test in §4.2.
    /// </summary>
    [Fact]
    public async Task Cookie_SlidingExpiration_Issues_Renewed_SetCookie()
    {
        // Use a tiny sliding window (2 s) so the 50%-elapsed threshold is crossed
        // immediately after sign-in.
        var (server, rootServices) = CreateServer(
            useSlidingExpiration: true,
            slidingDuration: TimeSpan.FromSeconds(2));
        using var _ = server;

        var (client, _) = await SignInAsync(server, rootServices);

        // Wait just over half the sliding window (1 s) so the middleware will renew.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the session must still be valid within the sliding window");
        // After >50% of the window has elapsed the middleware issues a Set-Cookie renewal.
        response.Headers.Contains("Set-Cookie").Should().BeTrue(
            "sliding expiration should renew the cookie once >50% of ExpireTimeSpan has elapsed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 105 — No SlidingExpiration → no renewed Set-Cookie on request
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When UseSlidingExpiration = false the cookie middleware must NOT issue a
    /// Set-Cookie on subsequent authenticated requests (the ticket expires at
    /// the original absolute time).
    /// </summary>
    [Fact]
    public async Task Cookie_No_SlidingExpiration_Does_Not_Renew_Cookie()
    {
        var (server, rootServices) = CreateServer(useSlidingExpiration: false);
        using var _ = server;

        var (client, _) = await SignInAsync(server, rootServices);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Without sliding expiry the auth cookie should not be renewed in the response.
        response.Headers.Contains("Set-Cookie").Should().BeFalse(
            "without sliding expiration the auth cookie should not be renewed per-request");
    }
}
